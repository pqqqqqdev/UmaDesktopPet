using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;

namespace UmaDesktopPet.Standalone.Runtime
{
    /// <summary>
    /// One authored facial or ear companion track. The installed mini-motion
    /// format supplies an AnimationClip triplet plus matching proprietary driven
    /// metadata. Both are required for compatibility, while the source clips are
    /// the runtime animation layer.
    /// </summary>
    public sealed class PetSynchronizedMotionTrackProfile
    {
        public PetSynchronizedMotionTrackProfile(
            string startAsset,
            string loopAsset,
            string endAsset,
            string drivenStartAsset,
            string drivenLoopAsset,
            string drivenEndAsset)
        {
            if (string.IsNullOrWhiteSpace(startAsset) ||
                string.IsNullOrWhiteSpace(loopAsset) ||
                string.IsNullOrWhiteSpace(endAsset) ||
                string.IsNullOrWhiteSpace(drivenStartAsset) ||
                string.IsNullOrWhiteSpace(drivenLoopAsset) ||
                string.IsNullOrWhiteSpace(drivenEndAsset))
            {
                throw new ArgumentException(
                    "A synchronized motion track requires exact source and driven " +
                    "start, loop, and end assets.");
            }

            StartAsset = startAsset.Trim();
            LoopAsset = loopAsset.Trim();
            EndAsset = endAsset.Trim();
            DrivenStartAsset = drivenStartAsset.Trim();
            DrivenLoopAsset = drivenLoopAsset.Trim();
            DrivenEndAsset = drivenEndAsset.Trim();
        }

        public string StartAsset { get; private set; }
        public string LoopAsset { get; private set; }
        public string EndAsset { get; private set; }
        public string DrivenStartAsset { get; private set; }
        public string DrivenLoopAsset { get; private set; }
        public string DrivenEndAsset { get; private set; }

        public IEnumerable<string> EnumerateRequiredAssets()
        {
            yield return StartAsset;
            yield return LoopAsset;
            yield return EndAsset;
            yield return DrivenStartAsset;
            yield return DrivenLoopAsset;
            yield return DrivenEndAsset;
        }
    }

    /// <summary>
    /// App-owned recipe for one complete installed mini-motion triplet. It stores
    /// stable logical names only; motion clips remain in the user's game bundles.
    /// </summary>
    public sealed class PetMotionSequenceProfile
    {
        public PetMotionSequenceProfile(
            string name,
            string displayName,
            string startAsset,
            string loopAsset,
            string endAsset,
            MiniFaceExpression expression,
            int loopRepetitions = 1,
            PetSynchronizedMotionTrackProfile faceTrack = null,
            PetSynchronizedMotionTrackProfile earTrack = null)
        {
            if (string.IsNullOrWhiteSpace(name) ||
                string.IsNullOrWhiteSpace(displayName) ||
                string.IsNullOrWhiteSpace(startAsset) ||
                string.IsNullOrWhiteSpace(loopAsset) ||
                string.IsNullOrWhiteSpace(endAsset))
            {
                throw new ArgumentException(
                    "A named motion sequence requires exact start, loop, and end assets.");
            }
            if (loopRepetitions < 1 || loopRepetitions > 4)
            {
                throw new ArgumentOutOfRangeException(
                    "loopRepetitions",
                    "A motion sequence may repeat its authored loop 1 to 4 times.");
            }
            if ((faceTrack == null) != (earTrack == null))
            {
                throw new ArgumentException(
                    "Synchronized mini motions require both face and ear tracks.");
            }

            Name = name.Trim();
            DisplayName = displayName.Trim();
            StartAsset = startAsset.Trim();
            LoopAsset = loopAsset.Trim();
            EndAsset = endAsset.Trim();
            Expression = expression;
            LoopRepetitions = loopRepetitions;
            FaceTrack = faceTrack;
            EarTrack = earTrack;
        }

        public string Name { get; private set; }
        public string DisplayName { get; private set; }
        public string StartAsset { get; private set; }
        public string LoopAsset { get; private set; }
        public string EndAsset { get; private set; }
        public MiniFaceExpression Expression { get; private set; }
        public int LoopRepetitions { get; private set; }
        public PetSynchronizedMotionTrackProfile FaceTrack { get; private set; }
        public PetSynchronizedMotionTrackProfile EarTrack { get; private set; }

        public bool HasSynchronizedCompanionTracks
        {
            get { return FaceTrack != null && EarTrack != null; }
        }

        public IEnumerable<string> EnumerateRequiredAssets()
        {
            yield return StartAsset;
            yield return LoopAsset;
            yield return EndAsset;
            if (!HasSynchronizedCompanionTracks)
            {
                yield break;
            }

            foreach (string asset in FaceTrack.EnumerateRequiredAssets())
            {
                yield return asset;
            }
            foreach (string asset in EarTrack.EnumerateRequiredAssets())
            {
                yield return asset;
            }
        }
    }

    /// <summary>
    /// Character-specific transition and personality motions. Shared reactions
    /// remain in the animation controller, while these clips keep each supported
    /// desktop Uma from behaving like a model swap.
    /// </summary>
    public sealed class PetCharacterMotionProfile
    {
        private readonly IReadOnlyList<PetAmbientVignetteProfile> _ambientVignettes;

        internal PetCharacterMotionProfile(
            string characterKey,
            int characterId,
            PetMotionSequenceProfile idle,
            PetMotionSequenceProfile tap,
            IEnumerable<PetAmbientVignetteProfile> ambientVignettes)
        {
            if (string.IsNullOrWhiteSpace(characterKey))
            {
                throw new ArgumentException("A character key is required.", "characterKey");
            }
            if (characterId <= 0 || characterId > 9999)
            {
                throw new ArgumentOutOfRangeException("characterId");
            }
            if (idle == null)
            {
                throw new ArgumentNullException("idle");
            }
            if (tap == null)
            {
                throw new ArgumentNullException("tap");
            }
            if (ambientVignettes == null)
            {
                throw new ArgumentNullException("ambientVignettes");
            }

            var orderedVignettes = new List<PetAmbientVignetteProfile>();
            var vignetteIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (PetAmbientVignetteProfile vignette in ambientVignettes)
            {
                if (vignette == null)
                {
                    throw new ArgumentException(
                        "Character ambient vignettes cannot contain null.",
                        "ambientVignettes");
                }
                if (!vignetteIds.Add(vignette.Id))
                {
                    throw new ArgumentException(
                        "A character motion profile repeats ambient vignette ID " +
                        vignette.Id + ".",
                        "ambientVignettes");
                }
                orderedVignettes.Add(vignette);
            }

            CharacterKey = characterKey.Trim();
            CharacterId = characterId;
            Idle = idle;
            Tap = tap;
            _ambientVignettes =
                new ReadOnlyCollection<PetAmbientVignetteProfile>(orderedVignettes);
        }

        public string CharacterKey { get; private set; }
        public int CharacterId { get; private set; }
        public PetMotionSequenceProfile Idle { get; private set; }
        public PetMotionSequenceProfile Tap { get; private set; }
        public IReadOnlyList<PetAmbientVignetteProfile> AmbientVignettes
        {
            get { return _ambientVignettes; }
        }
    }

    public static class PetCharacterMotionProfiles
    {
        public static readonly PetCharacterMotionProfile Oguri = Create(
            PetCharacterCatalog.Oguri,
            new PetAmbientVignetteProfile[0]);

        public static readonly PetCharacterMotionProfile Tachyon = Create(
            PetCharacterCatalog.Tachyon,
            new[] { CreateTachyonExperiment() });

        private static readonly IReadOnlyDictionary<string, PetCharacterMotionProfile>
            Profiles = new ReadOnlyDictionary<string, PetCharacterMotionProfile>(
                new Dictionary<string, PetCharacterMotionProfile>(StringComparer.Ordinal)
                {
                    { Oguri.CharacterKey, Oguri },
                    { Tachyon.CharacterKey, Tachyon }
                });

        public static bool TryGet(
            string characterKey,
            out PetCharacterMotionProfile profile)
        {
            if (string.IsNullOrWhiteSpace(characterKey))
            {
                profile = null;
                return false;
            }
            return Profiles.TryGetValue(characterKey.Trim(), out profile);
        }

        private static PetCharacterMotionProfile Create(
            PetCharacterProfile character,
            IEnumerable<PetAmbientVignetteProfile> ambientVignettes)
        {
            return new PetCharacterMotionProfile(
                character.Key,
                character.GameCharacterId,
                CharacterSequence(
                    character.GameCharacterId,
                    "idle01",
                    "Idle",
                    "Idle",
                    MiniFaceExpression.Neutral),
                CharacterSequence(
                    character.GameCharacterId,
                    "res01",
                    "TapReaction",
                    "Tap",
                    MiniFaceExpression.Tap),
                ambientVignettes);
        }

        private static PetAmbientVignetteProfile CreateTachyonExperiment()
        {
            return new PetAmbientVignetteProfile(
                PetCharacterDefinitions.TachyonExperimentId,
                "Experiment",
                PetCharacterDefinitions.TachyonExperimentRecipeId,
                1.0f,
                new[]
                {
                    new PetAmbientVignetteStepProfile(
                        PetCharacterDefinitions.TachyonResearchStepId,
                        SynchronizedSequence(
                            "TachyonExperimentResearch",
                            "Research",
                            "3d/motion/mini/event/body/type00/" +
                                "anm_min_eve_type00_near06",
                            "3d/motion/mini/event/facial/type00/" +
                                "anm_min_eve_type00_near06"),
                        false),
                    new PetAmbientVignetteStepProfile(
                        PetCharacterDefinitions.TachyonBreakthroughStepId,
                        SynchronizedSequence(
                            "TachyonExperimentBreakthrough",
                            "Experiment Result",
                            "3d/motion/mini/event/body/chara/chr1032_00/" +
                                "anm_min_eve_chr1032_00_pdk02",
                            "3d/motion/mini/event/facial/chara/chr1032_00/" +
                                "anm_min_eve_chr1032_00_pdk02"),
                        false)
                });
        }

        private static PetMotionSequenceProfile SynchronizedSequence(
            string name,
            string displayName,
            string bodyRoot,
            string facialRoot)
        {
            return new PetMotionSequenceProfile(
                name,
                displayName,
                bodyRoot + "_s",
                bodyRoot + "_loop",
                bodyRoot + "_e",
                MiniFaceExpression.Neutral,
                1,
                SynchronizedTrack(facialRoot, "face"),
                SynchronizedTrack(facialRoot, "ear"));
        }

        private static PetSynchronizedMotionTrackProfile SynchronizedTrack(
            string facialRoot,
            string trackToken)
        {
            return new PetSynchronizedMotionTrackProfile(
                facialRoot + "_s_" + trackToken,
                facialRoot + "_loop_" + trackToken,
                facialRoot + "_e_" + trackToken,
                facialRoot + "_s_" + trackToken + "_driven",
                facialRoot + "_loop_" + trackToken + "_driven",
                facialRoot + "_e_" + trackToken + "_driven");
        }

        private static PetMotionSequenceProfile CharacterSequence(
            int characterId,
            string motionToken,
            string name,
            string displayName,
            MiniFaceExpression expression,
            int loopRepetitions = 1)
        {
            string characterToken = characterId.ToString(
                "0000",
                CultureInfo.InvariantCulture);
            string root =
                "3d/motion/mini/event/body/chara/chr" + characterToken +
                "_00/anm_min_eve_chr" + characterToken + "_00_" + motionToken;
            return new PetMotionSequenceProfile(
                name,
                displayName,
                root + "_s",
                root + "_loop",
                root + "_e",
                expression,
                loopRepetitions);
        }
    }
}
