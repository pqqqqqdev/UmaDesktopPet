using System;
using System.IO;

namespace UmaDesktopPet.Standalone.Core
{
    public sealed class EncryptedAssetStream : Stream
    {
        private const int PlainHeaderLength = 256;

        private static readonly byte[] BundleBaseKey =
        {
            0x53, 0x2B, 0x46, 0x31, 0xE4, 0xA7,
            0xB9, 0x47, 0x3E, 0x7C, 0xFB
        };

        private readonly object _gate = new object();
        private readonly FileStream _inner;
        private readonly byte[] _xorKey;

        public EncryptedAssetStream(string path, long fileKey)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                throw new ArgumentException("An asset path is required.", "path");
            }

            _inner = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.Read,
                64 * 1024,
                FileOptions.RandomAccess);
            _xorKey = fileKey == 0 ? null : DeriveXorKey(fileKey);
        }

        public override bool CanRead { get { return true; } }
        public override bool CanSeek { get { return true; } }
        public override bool CanWrite { get { return false; } }

        public override long Length
        {
            get
            {
                lock (_gate)
                {
                    return _inner.Length;
                }
            }
        }

        public override long Position
        {
            get
            {
                lock (_gate)
                {
                    return _inner.Position;
                }
            }
            set
            {
                lock (_gate)
                {
                    _inner.Position = value;
                }
            }
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (buffer == null)
            {
                throw new ArgumentNullException("buffer");
            }
            if (offset < 0 || count < 0 || offset + count > buffer.Length)
            {
                throw new ArgumentOutOfRangeException("offset");
            }

            lock (_gate)
            {
                long start = _inner.Position;
                int read = _inner.Read(buffer, offset, count);
                if (_xorKey == null)
                {
                    return read;
                }

                for (int index = 0; index < read; index++)
                {
                    long absolute = start + index;
                    if (absolute < PlainHeaderLength)
                    {
                        continue;
                    }
                    buffer[offset + index] ^= _xorKey[(int)(absolute % _xorKey.Length)];
                }
                return read;
            }
        }

        public override long Seek(long offset, SeekOrigin origin)
        {
            lock (_gate)
            {
                return _inner.Seek(offset, origin);
            }
        }

        public override void Flush()
        {
        }

        public override void SetLength(long value)
        {
            throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            throw new NotSupportedException();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                lock (_gate)
                {
                    _inner.Dispose();
                }
            }
            base.Dispose(disposing);
        }

        private static byte[] DeriveXorKey(long fileKey)
        {
            byte[] keyBytes = BitConverter.GetBytes(fileKey);
            if (!BitConverter.IsLittleEndian)
            {
                Array.Reverse(keyBytes);
            }

            byte[] result = new byte[BundleBaseKey.Length * keyBytes.Length];
            for (int baseIndex = 0; baseIndex < BundleBaseKey.Length; baseIndex++)
            {
                for (int keyIndex = 0; keyIndex < keyBytes.Length; keyIndex++)
                {
                    result[(baseIndex * keyBytes.Length) + keyIndex] =
                        (byte)(BundleBaseKey[baseIndex] ^ keyBytes[keyIndex]);
                }
            }
            return result;
        }
    }
}
