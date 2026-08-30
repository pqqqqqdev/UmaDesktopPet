using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free checks for character-specific installed mini-motion
    /// recipes. These verify names only and never read or copy game assets.
    /// </summary>
    public static class PetCharacterMotionProfileSmokeTests
    {
        public static void Run()
        {
            PetCharacterMotionProfile profile;
            Assert(
                PetCharacterMotionProfiles.TryGet(
                    PetCharacterCatalog.Oguri.Key,
                    out profile),
                "Oguri motion profile should resolve");
            AssertEqual(
                PetCharacterMotionProfiles.Oguri,
                profile,
                "resolved Oguri motion profile");
            AssertCharacterProfile(
                profile,
                PetCharacterCatalog.Oguri,
                "1006");
            AssertEqual(
                0,
                profile.AmbientVignettes.Count,
                "Oguri ambient vignette count");

            Assert(
                PetCharacterMotionProfiles.TryGet(
                    PetCharacterCatalog.Tachyon.Key,
                    out profile),
                "Tachyon motion profile should resolve");
            AssertEqual(
                PetCharacterMotionProfiles.Tachyon,
                profile,
                "resolved Tachyon motion profile");
            AssertCharacterProfile(
                profile,
                PetCharacterCatalog.Tachyon,
                "1032");

            AssertEqual(
                1,
                profile.AmbientVignettes.Count,
                "Tachyon ambient vignette count");
            PetAmbientVignetteProfile experiment = profile.AmbientVignettes[0];
            AssertEqual(
                PetCharacterDefinitions.TachyonExperimentId,
                experiment.Id,
                "Tachyon experiment ID");
            AssertEqual("Experiment", experiment.DisplayName, "Tachyon experiment label");
            AssertEqual(
                PetCharacterDefinitions.TachyonExperimentRecipeId,
                experiment.VisualRecipeId,
                "Tachyon experiment visual recipe");
            AssertEqual(
                "installed-3d-laboratory",
                PetCharacterDefinitions.TachyonExperimentSceneVisualId,
                "Tachyon experiment installed 3D scene visual ID");
            AssertEqual(1.0f, experiment.AutonomyWeight, "Tachyon autonomy weight");
            AssertEqual(2, experiment.Steps.Count, "Tachyon experiment step count");

            PetAmbientVignetteStepProfile research = experiment.Steps[0];
            AssertEqual("research", research.Id, "research step ID");
            Assert(
                !research.TransitionDirectlyToNext,
                "research should play its authored end before breakthrough");
            AssertEqual(
                "TachyonExperimentResearch",
                research.Motion.Name,
                "research motion name");
            AssertEqual(
                MiniFaceExpression.Neutral,
                research.Motion.Expression,
                "research expression");
            AssertSequence(
                research.Motion,
                "3d/motion/mini/event/body/type00/anm_min_eve_type00_near06",
                "Tachyon research");
            AssertSynchronizedSequence(
                research.Motion,
                "3d/motion/mini/event/facial/type00/" +
                    "anm_min_eve_type00_near06",
                "Tachyon research");
            AssertEqual(1, research.Motion.LoopRepetitions, "research loop count");

            PetAmbientVignetteStepProfile breakthrough = experiment.Steps[1];
            AssertEqual("breakthrough", breakthrough.Id, "breakthrough step ID");
            Assert(
                !breakthrough.TransitionDirectlyToNext,
                "breakthrough should play its authored end");
            AssertEqual(
                "TachyonExperimentBreakthrough",
                breakthrough.Motion.Name,
                "breakthrough motion name");
            AssertEqual(
                MiniFaceExpression.Neutral,
                breakthrough.Motion.Expression,
                "breakthrough expression");
            AssertSequence(
                breakthrough.Motion,
                CharacterMotionRoot("1032", "pdk02"),
                "Tachyon breakthrough");
            AssertSynchronizedSequence(
                breakthrough.Motion,
                "3d/motion/mini/event/facial/chara/chr1032_00/" +
                    "anm_min_eve_chr1032_00_pdk02",
                "Tachyon breakthrough");
            AssertEqual(
                1,
                breakthrough.Motion.LoopRepetitions,
                "breakthrough loop count");
            string[] experimentAssets = experiment.Steps
                .SelectMany(step => step.Motion.EnumerateRequiredAssets())
                .ToArray();
            AssertEqual(30, experimentAssets.Length, "experiment required asset count");
            AssertEqual(
                experimentAssets.Length,
                experimentAssets.Distinct(StringComparer.OrdinalIgnoreCase).Count(),
                "experiment required assets should be unique");
            Assert(
                !experimentAssets.Any(asset =>
                    asset.IndexOf("mot03", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    asset.IndexOf("_book_", StringComparison.OrdinalIgnoreCase) >= 0),
                "the authentic experiment should not retain mot03 or book assets");

            IList<PetAmbientVignetteProfile> vignetteList =
                profile.AmbientVignettes as IList<PetAmbientVignetteProfile>;
            Assert(
                vignetteList != null && vignetteList.IsReadOnly,
                "character ambient vignette list should be immutable");
            IList<PetAmbientVignetteStepProfile> stepList =
                experiment.Steps as IList<PetAmbientVignetteStepProfile>;
            Assert(
                stepList != null && stepList.IsReadOnly,
                "ambient vignette step list should be immutable");

            PetAmbientVignetteStage[] lifecycleStages =
                (PetAmbientVignetteStage[])Enum.GetValues(
                    typeof(PetAmbientVignetteStage));
            AssertEqual(6, lifecycleStages.Length, "ambient lifecycle stage count");
            for (int stageIndex = 0;
                stageIndex < lifecycleStages.Length;
                stageIndex++)
            {
                var lifecycleEvent = new PetAmbientVignetteEvent(
                    lifecycleStages[stageIndex],
                    experiment.Id,
                    research.Id);
                AssertEqual(
                    lifecycleStages[stageIndex],
                    lifecycleEvent.Stage,
                    "lifecycle stage");
                AssertEqual(
                    experiment.Id,
                    lifecycleEvent.VignetteId,
                    "lifecycle vignette ID");
                AssertEqual(
                    research.Id,
                    lifecycleEvent.StepId,
                    "lifecycle step ID");
            }

            AssertInternalMotionMethod(
                "TryPlayAmbientVignette",
                typeof(string));
            AssertInternalMotionMethod("CancelAmbientVignette");
            AssertNoMotionMethod("TriggerAmbientSignature");
            AssertNoMotionMethod("CancelAmbientSignature");

            ExpectArgumentFailure(delegate
            {
                new PetAmbientVignetteProfile(
                    "bad-final-transition",
                    "Bad",
                    "bad",
                    1.0f,
                    new[]
                    {
                        new PetAmbientVignetteStepProfile(
                            "only",
                            research.Motion,
                            true)
                    });
            }, "a final step must not transition to a missing next step");
            ExpectArgumentFailure(delegate
            {
                new PetAmbientVignetteProfile(
                    "duplicate-step",
                    "Duplicate",
                    "duplicate",
                    1.0f,
                    new[] { research, research });
            }, "duplicate vignette step IDs must fail closed");
            ExpectArgumentFailure(delegate
            {
                new PetMotionSequenceProfile(
                    "missing-ear",
                    "Missing ear",
                    research.Motion.StartAsset,
                    research.Motion.LoopAsset,
                    research.Motion.EndAsset,
                    MiniFaceExpression.Neutral,
                    1,
                    research.Motion.FaceTrack,
                    null);
            }, "a synchronized sequence missing its ear track must fail closed");

            Assert(
                !PetCharacterMotionProfiles.TryGet("not-supported", out profile),
                "unsupported motion profiles must not resolve");
            Assert(profile == null, "unsupported motion profile should be null");
            Assert(
                !PetCharacterMotionProfiles.TryGet(null, out profile),
                "null motion profile keys must not resolve");
            Assert(profile == null, "null-key motion profile should be null");

            Debug.Log("Pet character motion-profile smoke tests passed.");
        }

        private static void ExpectArgumentFailure(Action action, string message)
        {
            try
            {
                action();
            }
            catch (ArgumentException)
            {
                return;
            }
            throw new InvalidOperationException(message);
        }

        private static void AssertInternalMotionMethod(
            string methodName,
            params Type[] parameterTypes)
        {
            MethodInfo method = typeof(OguriPetAnimationController).GetMethod(
                methodName,
                BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic,
                null,
                parameterTypes,
                null);
            Assert(method != null, methodName + " should exist");
            Assert(
                method.IsAssembly,
                methodName + " should be an assembly-internal presenter seam");
        }

        private static void AssertNoMotionMethod(string methodName)
        {
            MethodInfo method = typeof(OguriPetAnimationController).GetMethod(
                methodName,
                BindingFlags.Instance |
                    BindingFlags.Public |
                    BindingFlags.NonPublic);
            Assert(
                method == null,
                methodName + " should not bypass the signature-feature host");
        }

        private static void AssertCharacterProfile(
            PetCharacterMotionProfile profile,
            PetCharacterProfile character,
            string characterToken)
        {
            AssertEqual(character.Key, profile.CharacterKey, "motion character key");
            AssertEqual(
                character.GameCharacterId,
                profile.CharacterId,
                "motion character ID");

            AssertEqual("Idle", profile.Idle.Name, "idle name");
            AssertEqual("Idle", profile.Idle.DisplayName, "idle label");
            AssertEqual(
                MiniFaceExpression.Neutral,
                profile.Idle.Expression,
                "idle expression");
            AssertSequence(
                profile.Idle,
                CharacterMotionRoot(characterToken, "idle01"),
                character.ShortName + " idle");

            AssertEqual("TapReaction", profile.Tap.Name, "tap name");
            AssertEqual("Tap", profile.Tap.DisplayName, "tap label");
            AssertEqual(
                MiniFaceExpression.Tap,
                profile.Tap.Expression,
                "tap expression");
            AssertSequence(
                profile.Tap,
                CharacterMotionRoot(characterToken, "res01"),
                character.ShortName + " tap");
        }

        private static string CharacterMotionRoot(
            string characterToken,
            string motionToken)
        {
            return
                "3d/motion/mini/event/body/chara/chr" + characterToken +
                "_00/anm_min_eve_chr" + characterToken + "_00_" + motionToken;
        }

        private static void AssertSequence(
            PetMotionSequenceProfile sequence,
            string expectedRoot,
            string name)
        {
            Assert(sequence != null, name + " sequence should exist");
            AssertEqual(expectedRoot + "_s", sequence.StartAsset, name + " start asset");
            AssertEqual(expectedRoot + "_loop", sequence.LoopAsset, name + " loop asset");
            AssertEqual(expectedRoot + "_e", sequence.EndAsset, name + " end asset");
            Assert(sequence.LoopRepetitions >= 1, name + " loop count should be positive");
        }

        private static void AssertSynchronizedSequence(
            PetMotionSequenceProfile sequence,
            string expectedFacialRoot,
            string name)
        {
            Assert(
                sequence.HasSynchronizedCompanionTracks,
                name + " should have synchronized companion tracks");
            AssertTrack(sequence.FaceTrack, expectedFacialRoot, "face", name + " face");
            AssertTrack(sequence.EarTrack, expectedFacialRoot, "ear", name + " ear");
            AssertEqual(
                15,
                sequence.EnumerateRequiredAssets().Count(),
                name + " required asset count");
        }

        private static void AssertTrack(
            PetSynchronizedMotionTrackProfile track,
            string expectedRoot,
            string trackToken,
            string name)
        {
            Assert(track != null, name + " track should exist");
            AssertEqual(
                expectedRoot + "_s_" + trackToken,
                track.StartAsset,
                name + " runtime start clip");
            AssertEqual(
                expectedRoot + "_loop_" + trackToken,
                track.LoopAsset,
                name + " runtime loop clip");
            AssertEqual(
                expectedRoot + "_e_" + trackToken,
                track.EndAsset,
                name + " runtime end clip");
            AssertEqual(
                expectedRoot + "_s_" + trackToken + "_driven",
                track.DrivenStartAsset,
                name + " driven start metadata");
            AssertEqual(
                expectedRoot + "_loop_" + trackToken + "_driven",
                track.DrivenLoopAsset,
                name + " driven loop metadata");
            AssertEqual(
                expectedRoot + "_e_" + trackToken + "_driven",
                track.DrivenEndAsset,
                name + " driven end metadata");
            AssertEqual(
                6,
                track.EnumerateRequiredAssets().Count(),
                name + " complete runtime-and-metadata asset count");
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertEqual<T>(T expected, T actual, string name)
        {
            if (!Equals(expected, actual))
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }
    }
}
