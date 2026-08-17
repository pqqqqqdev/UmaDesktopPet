using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using UnityEngine;
using UmaDesktopPet.Standalone.Core;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// Loads installed game bundles without copying or exporting them. A lease keeps
    /// the source streams alive for as long as Unity may need to read from them.
    /// </summary>
    public sealed class BundleRepository : IDisposable
    {
        public const string DefaultShaderBundleName = "shader";

        private const uint ManagedReadBufferSize = 64 * 1024;

        private readonly GameDataCatalog _catalog;
        private readonly bool _ownsCatalog;
        private readonly int _unityThreadId;
        private readonly Dictionary<string, LoadedBundle> _loaded;
        private readonly List<string> _loadSequence;
        private bool _disposed;

        public BundleRepository(GameDataCatalog catalog)
            : this(catalog, false)
        {
        }

        public BundleRepository(GameDataCatalog catalog, bool ownsCatalog)
        {
            if (catalog == null)
            {
                throw new ArgumentNullException("catalog");
            }

            _catalog = catalog;
            _ownsCatalog = ownsCatalog;
            _unityThreadId = Thread.CurrentThread.ManagedThreadId;
            _loaded = new Dictionary<string, LoadedBundle>(StringComparer.Ordinal);
            _loadSequence = new List<string>();
        }

        public int LoadedBundleCount
        {
            get { return _loaded.Count; }
        }

        public BundleLease Acquire(string logicalName)
        {
            return AcquireMany(new[] { logicalName });
        }

        public BundleLease AcquireWithShaderFirst(string logicalName)
        {
            return AcquireManyWithShaderFirst(new[] { logicalName });
        }

        public BundleLease AcquireMany(IEnumerable<string> logicalNames)
        {
            return AcquireInternal(logicalNames, null);
        }

        /// <summary>
        /// Loads the shared shader bundle (and its prerequisites) before any of the
        /// requested roots. This is useful for game prefabs whose materials resolve
        /// their shaders while the prefab bundle is being loaded.
        /// </summary>
        public BundleLease AcquireManyWithShaderFirst(IEnumerable<string> logicalNames)
        {
            return AcquireInternal(logicalNames, DefaultShaderBundleName);
        }

        public bool IsLoaded(string logicalName)
        {
            if (logicalName == null)
            {
                throw new ArgumentNullException("logicalName");
            }
            EnsureUnityThread();
            ThrowIfDisposed();
            return _loaded.ContainsKey(logicalName);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureUnityThread();
            _disposed = true;

            Exception cleanupError = null;
            for (int index = _loadSequence.Count - 1; index >= 0; index--)
            {
                LoadedBundle loaded;
                if (_loaded.TryGetValue(_loadSequence[index], out loaded))
                {
                    cleanupError = Unload(loaded, cleanupError);
                }
            }
            _loaded.Clear();
            _loadSequence.Clear();

            if (_ownsCatalog)
            {
                try
                {
                    _catalog.Dispose();
                }
                catch (Exception exception)
                {
                    if (cleanupError == null)
                    {
                        cleanupError = exception;
                    }
                }
            }

            if (cleanupError != null)
            {
                throw cleanupError;
            }
        }

        internal void Release(IReadOnlyList<string> acquiredNames)
        {
            if (_disposed)
            {
                return;
            }

            EnsureUnityThread();
            Exception cleanupError = null;
            for (int index = acquiredNames.Count - 1; index >= 0; index--)
            {
                string logicalName = acquiredNames[index];
                LoadedBundle loaded;
                if (!_loaded.TryGetValue(logicalName, out loaded))
                {
                    continue;
                }

                loaded.ReferenceCount--;
                if (loaded.ReferenceCount < 0)
                {
                    throw new InvalidOperationException(
                        "Bundle reference count became negative: " + logicalName);
                }
                if (loaded.ReferenceCount != 0)
                {
                    continue;
                }

                _loaded.Remove(logicalName);
                _loadSequence.Remove(logicalName);
                cleanupError = Unload(loaded, cleanupError);
            }

            if (cleanupError != null)
            {
                throw cleanupError;
            }
        }

        private BundleLease AcquireInternal(
            IEnumerable<string> logicalNames,
            string firstLogicalName)
        {
            EnsureUnityThread();
            ThrowIfDisposed();

            List<string> rootNames = NormalizeRootNames(logicalNames);
            List<GameAssetRecord> loadOrder = BuildLoadOrder(rootNames, firstLogicalName);
            var acquiredNames = new List<string>(loadOrder.Count);

            try
            {
                foreach (GameAssetRecord record in loadOrder)
                {
                    LoadedBundle loaded;
                    if (!_loaded.TryGetValue(record.Name, out loaded))
                    {
                        loaded = Load(record);
                        _loaded.Add(record.Name, loaded);
                        _loadSequence.Add(record.Name);
                    }

                    loaded.ReferenceCount++;
                    acquiredNames.Add(record.Name);
                }

                var bundles = new Dictionary<string, AssetBundle>(StringComparer.Ordinal);
                foreach (string acquiredName in acquiredNames)
                {
                    bundles.Add(acquiredName, _loaded[acquiredName].Bundle);
                }
                return new BundleLease(this, rootNames, acquiredNames, bundles);
            }
            catch (Exception loadError)
            {
                try
                {
                    Release(acquiredNames);
                }
                catch (Exception cleanupError)
                {
                    throw new AggregateException(loadError, cleanupError);
                }
                throw;
            }
        }

        private List<GameAssetRecord> BuildLoadOrder(
            IEnumerable<string> rootNames,
            string firstLogicalName)
        {
            var result = new List<GameAssetRecord>();
            var included = new HashSet<string>(StringComparer.Ordinal);

            if (!string.IsNullOrEmpty(firstLogicalName))
            {
                AppendLoadOrder(_catalog.GetRequiredAsset(firstLogicalName), included, result);
            }
            foreach (string rootName in rootNames)
            {
                AppendLoadOrder(_catalog.GetRequiredAsset(rootName), included, result);
            }
            return result;
        }

        private void AppendLoadOrder(
            GameAssetRecord root,
            HashSet<string> included,
            List<GameAssetRecord> result)
        {
            foreach (GameAssetRecord record in _catalog.ResolveLoadOrder(root))
            {
                if (included.Add(record.Name))
                {
                    result.Add(record);
                }
            }
        }

        private static List<string> NormalizeRootNames(IEnumerable<string> logicalNames)
        {
            if (logicalNames == null)
            {
                throw new ArgumentNullException("logicalNames");
            }

            var result = new List<string>();
            var included = new HashSet<string>(StringComparer.Ordinal);
            foreach (string logicalName in logicalNames)
            {
                if (string.IsNullOrWhiteSpace(logicalName))
                {
                    throw new ArgumentException(
                        "Bundle logical names cannot be null or whitespace.",
                        "logicalNames");
                }
                if (included.Add(logicalName))
                {
                    result.Add(logicalName);
                }
            }

            if (result.Count == 0)
            {
                throw new ArgumentException(
                    "At least one bundle logical name is required.",
                    "logicalNames");
            }
            return result;
        }

        private static LoadedBundle Load(GameAssetRecord record)
        {
            Stream stream = null;
            try
            {
                stream = record.OpenRead();
                AssetBundle bundle = AssetBundle.LoadFromStream(
                    stream,
                    0,
                    ManagedReadBufferSize);
                if (bundle == null)
                {
                    throw new InvalidDataException(
                        "Unity could not load the installed asset bundle: " + record.Name);
                }

                return new LoadedBundle(record, stream, bundle);
            }
            catch
            {
                if (stream != null)
                {
                    stream.Dispose();
                }
                throw;
            }
        }

        private static Exception Unload(LoadedBundle loaded, Exception priorError)
        {
            try
            {
                if (loaded.Bundle != null)
                {
                    loaded.Bundle.Unload(false);
                }
            }
            catch (Exception exception)
            {
                if (priorError == null)
                {
                    priorError = exception;
                }
            }

            try
            {
                if (loaded.Stream != null)
                {
                    loaded.Stream.Dispose();
                }
            }
            catch (Exception exception)
            {
                if (priorError == null)
                {
                    priorError = exception;
                }
            }
            return priorError;
        }

        private void EnsureUnityThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != _unityThreadId)
            {
                throw new InvalidOperationException(
                    "AssetBundle load and unload operations must run on the Unity thread.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("BundleRepository");
            }
        }

        private sealed class LoadedBundle
        {
            public LoadedBundle(GameAssetRecord record, Stream stream, AssetBundle bundle)
            {
                Record = record;
                Stream = stream;
                Bundle = bundle;
            }

            public GameAssetRecord Record { get; private set; }
            public Stream Stream { get; private set; }
            public AssetBundle Bundle { get; private set; }
            public int ReferenceCount { get; set; }
        }
    }

    public sealed class BundleLease : IDisposable
    {
        private readonly BundleRepository _repository;
        private readonly IReadOnlyList<string> _rootNames;
        private readonly IReadOnlyList<string> _acquiredNames;
        private readonly IReadOnlyDictionary<string, AssetBundle> _bundles;
        private bool _disposed;

        internal BundleLease(
            BundleRepository repository,
            IList<string> rootNames,
            IList<string> acquiredNames,
            IDictionary<string, AssetBundle> bundles)
        {
            _repository = repository;
            _rootNames = new ReadOnlyCollection<string>(rootNames.ToList());
            _acquiredNames = new ReadOnlyCollection<string>(acquiredNames.ToList());
            _bundles = new ReadOnlyDictionary<string, AssetBundle>(
                new Dictionary<string, AssetBundle>(bundles, StringComparer.Ordinal));
        }

        public IReadOnlyList<string> RootNames
        {
            get { return _rootNames; }
        }

        public IReadOnlyList<string> LoadedBundleNames
        {
            get { return _acquiredNames; }
        }

        public AssetBundle RootBundle
        {
            get { return GetRequiredBundle(_rootNames[0]); }
        }

        public AssetBundle GetRequiredBundle(string logicalName)
        {
            ThrowIfDisposed();
            AssetBundle bundle;
            if (!_bundles.TryGetValue(logicalName, out bundle))
            {
                throw new KeyNotFoundException(
                    "Bundle is not part of this lease: " + logicalName);
            }
            return bundle;
        }

        public bool TryGetBundle(string logicalName, out AssetBundle bundle)
        {
            ThrowIfDisposed();
            return _bundles.TryGetValue(logicalName, out bundle);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _repository.Release(_acquiredNames);
            _disposed = true;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException("BundleLease");
            }
        }
    }
}
