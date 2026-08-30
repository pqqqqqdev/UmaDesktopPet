using System;
using System.Collections.Generic;
using System.IO;
using UmaDesktopPet.Standalone.Runtime;
using UnityEditor;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    public static class PetCharacterDefinitionSmokeTests
    {
        public static void Run()
        {
            AssertEqual(
                2,
                PetCharacterDefinitions.Selectable.Count,
                "selectable character-definition count");
            var selectableKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int index = 0;
                index < PetCharacterDefinitions.Selectable.Count;
                index++)
            {
                Assert(
                    selectableKeys.Add(
                        PetCharacterDefinitions.Selectable[index].Key),
                    "selectable character-definition keys must be unique");
            }
            Assert(
                ReferenceEquals(
                    PetCharacterDefinitions.Oguri,
                    PetCharacterDefinitions.Selectable[0]) &&
                ReferenceEquals(
                    PetCharacterDefinitions.Tachyon,
                    PetCharacterDefinitions.Selectable[1]),
                "selectable character-definition order should stay stable");
            IList<PetCharacterDefinition> selectable =
                PetCharacterDefinitions.Selectable as
                    IList<PetCharacterDefinition>;
            Assert(
                selectable != null && selectable.IsReadOnly,
                "the selectable character-definition registry should be immutable");

            AssertDefinition(
                PetCharacterDefinitions.Oguri,
                PetCharacterCatalog.Oguri,
                MiniCharacterAssemblyProfiles.OguriDefault,
                PetCharacterMotionProfiles.Oguri,
                PetAttachmentProfileCatalog.OguriCap,
                0);
            AssertDefinition(
                PetCharacterDefinitions.Tachyon,
                PetCharacterCatalog.Tachyon,
                MiniCharacterAssemblyProfiles.TachyonDefault,
                PetCharacterMotionProfiles.Tachyon,
                PetAttachmentProfileCatalog.AgnesTachyon,
                1);

            PetCharacterDefinition resolved;
            Assert(
                PetCharacterDefinitions.TryGet("agnes-tachyon", out resolved) &&
                ReferenceEquals(PetCharacterDefinitions.Tachyon, resolved),
                "Tachyon should resolve through the aggregate registry");
            Assert(
                !PetCharacterDefinitions.TryGet("not-supported", out resolved) &&
                resolved == null,
                "unknown character definitions must fail closed");
            Assert(
                ReferenceEquals(
                    PetCharacterDefinitions.Oguri,
                    PetCharacterDefinitions.ResolveOrDefault("not-supported")),
                "unknown saved keys should retain the explicit Oguri fallback");

            PetSignatureFeatureDefinition experiment =
                PetCharacterDefinitions.Tachyon.SignatureFeatures[0];
            Assert(
                ReferenceEquals(
                    PetCharacterDefinitions.Tachyon.Motions.AmbientVignettes[0],
                    experiment.Vignette),
                "the Tachyon definition should bind the exact configured " +
                "experiment motion");
            AssertEqual(
                PetCharacterDefinitions.TachyonExperimentId,
                experiment.Id,
                "experiment feature ID");
            AssertEqual("Experiment", experiment.DisplayName, "experiment label");
            AssertEqual(
                PetSignatureFeaturePresentationKind.InstalledPrefabScene,
                experiment.Presentation.Kind,
                "experiment presentation kind");
            AssertEqual(
                PetCharacterDefinitions.TachyonExperimentSceneVisualId,
                experiment.Presentation.VisualId,
                "experiment scene visual ID");
            AssertEqual(
                5,
                experiment.Presentation.InstalledSceneProps.Count,
                "experiment installed 3D prop count");
            IList<PetInstalledScenePropDefinition> sceneProps =
                experiment.Presentation.InstalledSceneProps as
                    IList<PetInstalledScenePropDefinition>;
            Assert(
                sceneProps != null && sceneProps.IsReadOnly,
                "experiment installed scene props should be immutable");
            AssertEqual(
                1,
                experiment.Presentation.AppOwnedSceneProps.Count,
                "experiment app-owned 3D prop count");
            IList<PetAppOwnedScenePropDefinition> appSceneProps =
                experiment.Presentation.AppOwnedSceneProps as
                    IList<PetAppOwnedScenePropDefinition>;
            Assert(
                appSceneProps != null && appSceneProps.IsReadOnly,
                "experiment app-owned scene props should be immutable");
            PetAppOwnedScenePropDefinition bench = appSceneProps[0];
            Assert(
                bench.Required &&
                bench.VisualId == PetCharacterDefinitions.TachyonLabTableVisualId &&
                bench.IsVisibleInStep(
                    PetCharacterDefinitions.TachyonResearchStepId) &&
                bench.IsVisibleInStep(
                    PetCharacterDefinitions.TachyonBreakthroughStepId),
                "the app-owned lab bench should be the required persistent anchor");
            AssertEqual(
                PetAppOwnedScenePropSourceKind.ResourcePrefab,
                bench.SourceKind,
                "lab bench geometry source");
            AssertEqual(0, bench.Parts.Count, "lab bench authored box count");
            AssertEqual(
                1,
                bench.SupportSurfaces.Count,
                "lab bench support-surface count");
            AssertEqual(
                new Vector3(0.29f, -0.36f, 0.04f),
                bench.OffsetInCharacterHeights,
                "lab bench character-relative origin");
            AssertApproximately(
                -0.07f,
                experiment.Presentation
                    .CharacterHorizontalOffsetInCharacterHeights,
                0.000001f,
                "experiment character presentation offset");
            AssertApproximately(
                0.02f,
                experiment.Presentation
                    .FramingHorizontalBiasInViewportWidths,
                0.000001f,
                "experiment viewport framing bias");
            AssertApproximately(
                PetCharacterDefinitions.TachyonLabBenchYawDegrees,
                bench.YawDegrees,
                0.000001f,
                "lab bench authored yaw");
            AssertEqual(
                new Color32(121, 166, 168, 255),
                bench.Palette.Worktop,
                "lab bench worktop color");
            AssertEqual(
                new Color32(84, 116, 124, 255),
                bench.Palette.Frame,
                "lab bench frame color");
            PetAppOwnedResourcePrefabDefinition benchResource =
                bench.ResourcePrefab;
            Assert(
                benchResource != null &&
                benchResource.ResourcePath ==
                    PetCharacterDefinitions.TachyonLabBenchResourcePath,
                "the lab bench should use the exact app-owned Resources path");
            AssertApproximately(
                0.28f,
                PetCharacterDefinitions
                    .TachyonLabBenchHeightInCharacterHeights,
                0.000001f,
                "lab bench declared normalized height");
            AssertApproximately(
                PetCharacterDefinitions
                    .TachyonLabBenchHeightInCharacterHeights,
                benchResource.DesiredHeightInCharacterHeights,
                0.000001f,
                "lab bench resource normalized height");
            string[] expectedMaterialNames =
            {
                "wood"
            };
            PetAppOwnedSceneMaterialRole[] expectedMaterialRoles =
            {
                PetAppOwnedSceneMaterialRole.Worktop
            };
            AssertEqual(
                expectedMaterialNames.Length,
                benchResource.MaterialMappings.Count,
                "lab bench exact source-material mapping count");
            for (int mappingIndex = 0;
                mappingIndex < expectedMaterialNames.Length;
                mappingIndex++)
            {
                PetAppOwnedSceneMaterialMappingDefinition mapping =
                    benchResource.MaterialMappings[mappingIndex];
                Assert(
                    mapping.SourceMaterialName ==
                        expectedMaterialNames[mappingIndex] &&
                    mapping.MaterialRole == expectedMaterialRoles[mappingIndex],
                    "lab bench source-material mapping " + mappingIndex +
                    " should remain exact");
            }
            PetAppOwnedSupportSurfaceDefinition worktop =
                bench.FindSupportSurface("worktop");
            Assert(
                worktop != null &&
                worktop.LocalCenterInCharacterHeights ==
                    new Vector3(-0.06f, 0.14f, 0.0f) &&
                worktop.SizeInCharacterHeights == new Vector2(0.16f, 0.16f),
                "the lab bench must expose the exact horizontal worktop");
            VerifyTachyonLabBenchAssetContract();
            AssertEqual(
                5,
                experiment.Presentation.InstalledSceneEffects.Count,
                "experiment installed formula-effect count");
            IList<PetInstalledSceneEffectDefinition> sceneEffects =
                experiment.Presentation.InstalledSceneEffects as
                    IList<PetInstalledSceneEffectDefinition>;
            Assert(
                sceneEffects != null && sceneEffects.IsReadOnly,
                "experiment installed scene effects should be immutable");
            AssertFormulaEffect(
                experiment,
                PetCharacterDefinitions.TachyonFormula01VisualId,
                "pfb_eff_chr1032_00_06",
                true,
                false,
                new Vector3(0.30f, 0.14f, 0.40f),
                0.34f);
            AssertFormulaEffect(
                experiment,
                PetCharacterDefinitions.TachyonFormula02VisualId,
                "pfb_eff_chr1032_00_07",
                true,
                false,
                new Vector3(0.33f, 0.10f, 0.41f),
                0.34f);
            AssertFormulaEffect(
                experiment,
                PetCharacterDefinitions.TachyonFormula03VisualId,
                "pfb_eff_chr1032_00_08",
                true,
                false,
                new Vector3(0.45f, 0.08f, 0.42f),
                0.34f);
            AssertFormulaEffect(
                experiment,
                PetCharacterDefinitions.TachyonFormula04VisualId,
                "pfb_eff_chr1032_00_10",
                false,
                true,
                new Vector3(0.192f, 0.39f, 0.41f),
                0.36f);
            AssertFormulaEffect(
                experiment,
                PetCharacterDefinitions.TachyonFormula05VisualId,
                "pfb_eff_chr1032_00_09",
                false,
                true,
                new Vector3(0.26f, 0.42f, 0.42f),
                0.48f);
            VerifyAppOwnedResourcePrefabContract();
            VerifyInstalledParticlePlanner();

            string[] reagentVisualIds =
            {
                PetCharacterDefinitions.TachyonCyanReagentVisualId,
                PetCharacterDefinitions.TachyonPurpleReagentVisualId,
                PetCharacterDefinitions.TachyonYellowReagentVisualId,
                PetCharacterDefinitions.TachyonAmberReagentVisualId,
                PetCharacterDefinitions.TachyonBlueReagentVisualId
            };
            float[] reagentWorktopU =
            {
                -0.30f,
                -0.30f,
                0.0f,
                0.30f,
                0.30f
            };
            float[] reagentWorktopV =
            {
                -0.25f,
                0.25f,
                0.0f,
                -0.25f,
                0.25f
            };
            for (int reagentIndex = 0;
                reagentIndex < reagentVisualIds.Length;
                reagentIndex++)
            {
                PetInstalledScenePropDefinition reagent = FindSceneProp(
                    experiment,
                    reagentVisualIds[reagentIndex]);
                string variant = reagentIndex.ToString("00");
                Assert(
                    reagent.Required &&
                    reagent.IsVisibleInStep(
                        PetCharacterDefinitions.TachyonResearchStepId) &&
                    reagent.IsVisibleInStep(
                        PetCharacterDefinitions.TachyonBreakthroughStepId) &&
                    reagent.Motion == PetInstalledScenePropMotion.None,
                    "each grounded bottle should persist unchanged across " +
                    "both experiment steps");
                Assert(
                    reagent.OffsetInCharacterHeights == Vector3.zero &&
                    reagent.SupportPlacement != null &&
                    reagent.SupportPlacement.SupportVisualId ==
                        PetCharacterDefinitions.TachyonLabTableVisualId &&
                    reagent.SupportPlacement.SurfaceId == "worktop",
                    "each bottle must use one app-owned support placement");
                AssertApproximately(
                    reagentWorktopU[reagentIndex],
                    reagent.SupportPlacement.NormalizedCoordinates.x,
                    0.000001f,
                    reagentVisualIds[reagentIndex] + " worktop U");
                AssertApproximately(
                    reagentWorktopV[reagentIndex],
                    reagent.SupportPlacement.NormalizedCoordinates.y,
                    0.000001f,
                    reagentVisualIds[reagentIndex] + " worktop V");
                AssertEqual(
                    new Vector3(
                        0.0f,
                        PetCharacterDefinitions.TachyonLabBenchYawDegrees,
                        0.0f),
                    reagent.EulerDegrees,
                    reagentVisualIds[reagentIndex] + " bench-aligned yaw");
                AssertEqual(
                    "3d/chara/prop/prop1143_" + variant +
                        "/pfb_chr_prop1143_" + variant,
                    reagent.LogicalName,
                    reagentVisualIds[reagentIndex] + " logical name");
            }
            for (int propIndex = 0;
                propIndex < experiment.Presentation.InstalledSceneProps.Count;
                propIndex++)
            {
                Assert(
                    experiment.Presentation.InstalledSceneProps[propIndex]
                        .LogicalName.StartsWith("3d/", StringComparison.Ordinal) &&
                    experiment.Presentation.InstalledSceneProps[propIndex]
                        .LogicalName.IndexOf(
                            "mahjong",
                            StringComparison.OrdinalIgnoreCase) < 0,
                    "experiment must contain only exact installed 3D bottles, " +
                    "never the rejected mahjong table");
            }
            VerifySceneSupportPlacementPlanner(bench, worktop);
            AssertEqual(
                "Assembling the laboratory",
                experiment.Presentation.ResolveStageLabel(
                    PetAmbientVignetteStage.Setup,
                    PetCharacterDefinitions.TachyonResearchStepId),
                "experiment setup label");
            AssertEqual(
                "Research",
                experiment.Presentation.ResolveStageLabel(
                    PetAmbientVignetteStage.StepLoop,
                    PetCharacterDefinitions.TachyonResearchStepId),
                "research loop label");
            AssertEqual(
                "Experiment result complete",
                experiment.Presentation.ResolveStageLabel(
                    PetAmbientVignetteStage.StepEnd,
                    PetCharacterDefinitions.TachyonBreakthroughStepId),
                "breakthrough end label");

            IList<PetSignatureFeatureDefinition> signatureList =
                PetCharacterDefinitions.Tachyon.SignatureFeatures as
                    IList<PetSignatureFeatureDefinition>;
            Assert(
                signatureList != null && signatureList.IsReadOnly,
                "character signature-feature definitions should be immutable");
            Assert(
                typeof(IPetSignatureFeatureHost).IsAssignableFrom(
                    typeof(PetAmbientVignettePresenter)),
                "the vignette presenter should expose the shared " +
                "signature-feature host contract");

            ExpectArgumentFailure(delegate
            {
                new PetCharacterDefinition(
                    PetCharacterCatalog.Oguri,
                    MiniCharacterAssemblyProfiles.TachyonDefault,
                    PetCharacterMotionProfiles.Oguri,
                    PetAttachmentProfileCatalog.OguriCap,
                    new PetSignatureFeatureDefinition[0]);
            }, "mixed character-definition parts must fail closed");

            ExpectArgumentFailure(delegate
            {
                new PetCharacterDefinition(
                    PetCharacterCatalog.Tachyon,
                    MiniCharacterAssemblyProfiles.TachyonDefault,
                    PetCharacterMotionProfiles.Tachyon,
                    PetAttachmentProfileCatalog.AgnesTachyon,
                    new PetSignatureFeatureDefinition[0]);
            }, "vignette motions without presentation definitions must fail closed");

            ExpectArgumentFailure(delegate
            {
                new PetSignatureFeatureDefinition(
                    experiment.Vignette,
                    new PetSignatureFeaturePresentation(
                        "wrong-recipe",
                        PetSignatureFeaturePresentationKind
                            .InstalledStudyFurniture,
                        "visual",
                        "Setup",
                        "Unavailable",
                        "Failed",
                        new[]
                        {
                            new PetSignatureFeatureStepPresentation(
                                PetCharacterDefinitions.TachyonResearchStepId,
                                "Start",
                                "Loop",
                                "End")
                        }));
            }, "mismatched motion/presentation recipe IDs must fail closed");

            ExpectArgumentFailure(delegate
            {
                new PetSignatureFeaturePresentation(
                    "invalid-character-offset",
                    PetSignatureFeaturePresentationKind
                        .InstalledStudyFurniture,
                    "visual",
                    "Setup",
                    "Unavailable",
                    "Failed",
                    new[]
                    {
                        new PetSignatureFeatureStepPresentation(
                            PetCharacterDefinitions.TachyonResearchStepId,
                            "Start",
                            "Loop",
                            "End")
                    },
                    null,
                    null,
                    null,
                    float.PositiveInfinity);
            }, "non-finite character presentation offsets must fail closed");

            ExpectArgumentFailure(delegate
            {
                new PetSignatureFeaturePresentation(
                    "invalid-framing-bias",
                    PetSignatureFeaturePresentationKind
                        .InstalledStudyFurniture,
                    "visual",
                    "Setup",
                    "Unavailable",
                    "Failed",
                    new[]
                    {
                        new PetSignatureFeatureStepPresentation(
                            PetCharacterDefinitions.TachyonResearchStepId,
                            "Start",
                            "Loop",
                            "End")
                    },
                    null,
                    null,
                    null,
                    0.0f,
                    float.PositiveInfinity);
            }, "non-finite viewport framing biases must fail closed");

            Debug.Log("Pet character-definition smoke tests passed.");
        }

        private static void AssertDefinition(
            PetCharacterDefinition definition,
            PetCharacterProfile profile,
            MiniCharacterAssemblyProfile assembly,
            PetCharacterMotionProfile motions,
            PetAttachmentProfile attachments,
            int expectedSignatureFeatureCount)
        {
            AssertEqual(profile.Key, definition.Key, "definition key");
            Assert(
                ReferenceEquals(profile, definition.Profile),
                "definition should expose the exact identity profile");
            Assert(
                ReferenceEquals(assembly, definition.Assembly),
                "definition should expose the exact assembly profile");
            Assert(
                ReferenceEquals(motions, definition.Motions),
                "definition should expose the exact motion profile");
            Assert(
                ReferenceEquals(attachments, definition.Attachments),
                "definition should expose the exact attachment profile");
            AssertEqual(
                expectedSignatureFeatureCount,
                definition.SignatureFeatures.Count,
                "definition signature-feature count");
        }

        private static PetInstalledScenePropDefinition FindSceneProp(
            PetSignatureFeatureDefinition feature,
            string visualId)
        {
            for (int index = 0;
                index < feature.Presentation.InstalledSceneProps.Count;
                index++)
            {
                PetInstalledScenePropDefinition candidate =
                    feature.Presentation.InstalledSceneProps[index];
                if (string.Equals(
                    candidate.VisualId,
                    visualId,
                    StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "Missing installed scene prop " + visualId + ".");
        }

        private static void VerifyTachyonLabBenchAssetContract()
        {
            const string prefabPath =
                "Assets/Resources/AppOwned/TachyonLabBench.prefab";
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                prefabPath);
            Assert(prefab != null, "the app-owned lab bench prefab is missing");

            MeshFilter[] filters =
                prefab.GetComponentsInChildren<MeshFilter>(true);
            MeshRenderer[] renderers =
                prefab.GetComponentsInChildren<MeshRenderer>(true);
            Assert(
                filters.Length == 1 && renderers.Length == 1 &&
                filters[0].gameObject == renderers[0].gameObject &&
                filters[0].sharedMesh != null && renderers[0].enabled,
                "the lab bench prefab must contain exactly one paired static " +
                "MeshFilter and MeshRenderer");
            Assert(
                prefab.GetComponentsInChildren<MonoBehaviour>(true).Length == 0 &&
                prefab.GetComponentsInChildren<Animator>(true).Length == 0 &&
                prefab.GetComponentsInChildren<Collider>(true).Length == 0 &&
                prefab.GetComponentsInChildren<ParticleSystem>(true).Length == 0 &&
                prefab.GetComponentsInChildren<Camera>(true).Length == 0 &&
                prefab.GetComponentsInChildren<Light>(true).Length == 0 &&
                prefab.GetComponentsInChildren<AudioSource>(true).Length == 0,
                "the lab bench prefab must remain static and script-free");
            Component[] components = prefab.GetComponentsInChildren<Component>(
                true);
            for (int index = 0; index < components.Length; index++)
            {
                Assert(
                    components[index] != null &&
                    (components[index] is Transform ||
                     components[index] is MeshFilter ||
                     components[index] is MeshRenderer),
                    "the lab bench prefab contains an unsupported component");
            }

            Mesh mesh = filters[0].sharedMesh;
            long triangleCount = 0L;
            for (int subMeshIndex = 0;
                subMeshIndex < mesh.subMeshCount;
                subMeshIndex++)
            {
                Assert(
                    mesh.GetTopology(subMeshIndex) == MeshTopology.Triangles,
                    "the lab bench mesh must contain triangle topology only");
                triangleCount += (long)mesh.GetIndexCount(subMeshIndex) / 3L;
            }
            Assert(
                mesh.vertexCount >= 48 && triangleCount >= 80L &&
                mesh.subMeshCount == 1,
                "the solid-top lab table mesh failed its audited geometry " +
                "contract");
            string meshAssetPath = AssetDatabase.GetAssetPath(mesh);
            Assert(
                !string.IsNullOrWhiteSpace(meshAssetPath) &&
                meshAssetPath.StartsWith(
                    "Assets/AppOwned/TachyonLabBench/",
                    StringComparison.Ordinal),
                "the lab bench prefab mesh must come from the audited app-owned " +
                "source directory");
            Shader surfaceShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/AppOwnedProp.shader");
            Shader outlineShader = AssetDatabase.LoadAssetAtPath<Shader>(
                "Assets/Resources/AppOwnedPropOutline.shader");
            Assert(
                surfaceShader != null && outlineShader != null,
                "the app-owned lab bench surface and outline shaders must both " +
                "be present");
        }

        private static void VerifyAppOwnedResourcePrefabContract()
        {
            var materialMappings = new[]
            {
                new PetAppOwnedSceneMaterialMappingDefinition(
                    "BenchTop",
                    PetAppOwnedSceneMaterialRole.Worktop),
                new PetAppOwnedSceneMaterialMappingDefinition(
                    "SteelFrame",
                    PetAppOwnedSceneMaterialRole.Frame)
            };
            var resource = new PetAppOwnedResourcePrefabDefinition(
                "AppOwned\\LabBench",
                0.62f,
                materialMappings);
            PetAppOwnedSceneMaterialRole role;
            Assert(
                resource.ResourcePath == "AppOwned/LabBench" &&
                resource.DesiredHeightInCharacterHeights == 0.62f &&
                resource.TryResolveMaterialRole("BenchTop", out role) &&
                role == PetAppOwnedSceneMaterialRole.Worktop &&
                !resource.TryResolveMaterialRole("benchtop", out role),
                "resource-prefab geometry should normalize its path and map " +
                "source materials by exact name");
            IList<PetAppOwnedSceneMaterialMappingDefinition> mappings =
                resource.MaterialMappings as
                    IList<PetAppOwnedSceneMaterialMappingDefinition>;
            Assert(
                mappings != null && mappings.IsReadOnly,
                "resource-prefab material mappings should be immutable");

            var palette = new PetAppOwnedScenePalette(
                new Color32(1, 2, 3, 255),
                new Color32(4, 5, 6, 255),
                new Color32(7, 8, 9, 255),
                new Color32(10, 11, 12, 255),
                new Color32(13, 14, 15, 255));
            PetAppOwnedScenePropDefinition prop =
                PetAppOwnedScenePropDefinition.CreateResourcePrefab(
                    "resource-contract",
                    true,
                    new[] { "loop" },
                    new Vector3(0.1f, -0.2f, 0.3f),
                    17.0f,
                    palette,
                    resource,
                    new[]
                    {
                        new PetAppOwnedSupportSurfaceDefinition(
                            "top",
                            new Vector3(0.0f, 0.2f, 0.0f),
                            new Vector2(0.5f, 0.3f))
                    });
            Assert(
                prop.SourceKind ==
                    PetAppOwnedScenePropSourceKind.ResourcePrefab &&
                ReferenceEquals(resource, prop.ResourcePrefab) &&
                prop.Parts.Count == 0 &&
                prop.YawDegrees == 17.0f &&
                prop.FindSupportSurface("top") != null,
                "resource-prefab scene props should retain their data-defined " +
                "source, placement, and support contract");

            ExpectArgumentFailure(delegate
            {
                new PetAppOwnedResourcePrefabDefinition(
                    "AppOwned/LabBench.prefab",
                    0.62f,
                    materialMappings);
            }, "resource-prefab paths with extensions must fail closed");
            ExpectArgumentFailure(delegate
            {
                new PetAppOwnedResourcePrefabDefinition(
                    "AppOwned/LabBench",
                    0.62f,
                    new[]
                    {
                        materialMappings[0],
                        new PetAppOwnedSceneMaterialMappingDefinition(
                            "BenchTop",
                            PetAppOwnedSceneMaterialRole.Frame)
                    });
            }, "duplicate source-material mappings must fail closed");
        }

        private static void VerifySceneSupportPlacementPlanner(
            PetAppOwnedScenePropDefinition bench,
            PetAppOwnedSupportSurfaceDefinition supportSurface)
        {
            const float characterHeight = 2.0f;
            Vector3 characterCenter = new Vector3(1.0f, 1.0f, 2.0f);
            var offCenterSource = new Bounds(
                new Vector3(4.0f, -3.0f, 8.0f),
                new Vector3(0.5f, 1.0f, 0.25f));
            var placement = new PetSceneSupportPlacementDefinition(
                bench.VisualId,
                supportSurface.SurfaceId,
                new Vector2(0.15f, 0.15f),
                0.0f);
            float surfaceY = characterCenter.y +
                (bench.OffsetInCharacterHeights.y +
                    supportSurface.LocalCenterInCharacterHeights.y) *
                characterHeight;

            Quaternion[] rotations =
            {
                Quaternion.identity,
                Quaternion.Euler(0.0f, 90.0f, 0.0f),
                Quaternion.Euler(19.0f, 37.0f, 11.0f)
            };
            for (int index = 0; index < rotations.Length; index++)
            {
                PetSceneSupportPlacementPlan plan =
                    PetSceneSupportPlacementPlanner.CreatePlan(
                        offCenterSource,
                        rotations[index],
                        0.068f,
                        characterHeight,
                        characterCenter,
                        bench,
                        supportSurface,
                        placement);
                AssertApproximately(
                    surfaceY,
                    plan.LocalCenter.y - plan.RotatedHalfExtents.y,
                    characterHeight * 0.00001f,
                    "surface planner bottom contact at rotation " + index);
            }

            PetSceneSupportPlacementPlan identity =
                PetSceneSupportPlacementPlanner.CreatePlan(
                    offCenterSource,
                    Quaternion.identity,
                    0.068f,
                    characterHeight,
                    characterCenter,
                    bench,
                    supportSurface,
                    placement);
            Quaternion supportRotation = Quaternion.Euler(
                0.0f,
                bench.YawDegrees,
                0.0f);
            Vector3 expectedSurfaceCenter = characterCenter +
                bench.OffsetInCharacterHeights * characterHeight +
                supportRotation *
                    (supportSurface.LocalCenterInCharacterHeights *
                        characterHeight);
            Vector3 expectedHorizontalCenter = expectedSurfaceCenter +
                supportRotation *
                    (Vector3.right *
                        (placement.NormalizedCoordinates.x *
                            supportSurface.SizeInCharacterHeights.x *
                            characterHeight) +
                     Vector3.forward *
                        (placement.NormalizedCoordinates.y *
                            supportSurface.SizeInCharacterHeights.y *
                            characterHeight));
            AssertApproximately(
                expectedHorizontalCenter.x,
                identity.LocalCenter.x,
                0.00001f,
                "surface planner horizontal coordinate");
            AssertApproximately(
                expectedHorizontalCenter.z,
                identity.LocalCenter.z,
                0.00001f,
                "surface planner depth coordinate");

            ExpectInvalidDataFailure(delegate
            {
                PetSceneSupportPlacementPlanner.CreatePlan(
                    new Bounds(Vector3.zero, Vector3.one),
                    Quaternion.identity,
                    0.30f,
                    characterHeight,
                    characterCenter,
                    bench,
                    supportSurface,
                    new PetSceneSupportPlacementDefinition(
                        bench.VisualId,
                        supportSurface.SurfaceId,
                        new Vector2(0.5f, 0.0f),
                        0.0f));
            }, "surface planner must reject physical overhang");
        }

        private static PetInstalledSceneEffectDefinition FindSceneEffect(
            PetSignatureFeatureDefinition feature,
            string visualId)
        {
            for (int index = 0;
                index < feature.Presentation.InstalledSceneEffects.Count;
                index++)
            {
                PetInstalledSceneEffectDefinition candidate =
                    feature.Presentation.InstalledSceneEffects[index];
                if (string.Equals(
                    candidate.VisualId,
                    visualId,
                    StringComparison.Ordinal))
                {
                    return candidate;
                }
            }
            throw new InvalidOperationException(
                "Missing installed scene effect " + visualId + ".");
        }

        private static void AssertFormulaEffect(
            PetSignatureFeatureDefinition feature,
            string visualId,
            string assetName,
            bool research,
            bool breakthrough,
            Vector3 expectedOffset,
            float expectedScale)
        {
            PetInstalledSceneEffectDefinition effect = FindSceneEffect(
                feature,
                visualId);
            Assert(effect.Required,
                "an incomplete installed formula set must fail the optional " +
                "Experiment feature closed");
            AssertEqual(assetName, effect.AssetName,
                visualId + " exact asset basename");
            AssertEqual(
                "3d/effect/charaskill/pfb_eff_chr1032_00/" + assetName,
                effect.LogicalName,
                visualId + " exact logical name");
            AssertEqual(
                PetInstalledSceneEffectActivation.IncludeExternallyTriggered,
                effect.Activation,
                visualId + " external-trigger activation");
            AssertEqual(
                PetInstalledSceneEffectPlayback.HoldWarmupSnapshot,
                effect.Playback,
                visualId + " stable-tableau playback");
            AssertApproximately(
                0.18f,
                effect.WarmupSeconds,
                0.000001f,
                visualId + " held-snapshot time");
            Assert(
                effect.IsVisibleInStep(
                    PetCharacterDefinitions.TachyonResearchStepId) == research &&
                effect.IsVisibleInStep(
                    PetCharacterDefinitions.TachyonBreakthroughStepId) ==
                    breakthrough,
                visualId + " step routing");
            AssertEqual(
                expectedOffset,
                effect.OffsetInCharacterHeights,
                visualId + " viewport-safe offset");
            AssertEqual(
                new Vector3(0.0f, 180.0f, 0.0f),
                effect.EulerDegrees,
                visualId + " camera-readable facing");
            AssertApproximately(
                expectedScale,
                effect.ScaleInCharacterHeights,
                0.000001f,
                visualId + " viewport-safe scale");
        }

        private static void VerifyInstalledParticlePlanner()
        {
            const string logicalName =
                "3d/effect/charaskill/test/pfb_eff_test";
            var descriptors = new[]
            {
                new InstalledParticleSystemDescriptor(
                    "auto", true, true, true, 128),
                new InstalledParticleSystemDescriptor(
                    "manual", true, false, true, 256),
                new InstalledParticleSystemDescriptor(
                    "inactive", false, true, true, 512),
                new InstalledParticleSystemDescriptor(
                    "hidden", true, true, false, 512)
            };
            InstalledParticleSystemPlan conservative =
                InstalledParticleEffectPlanner.BuildPlan(
                    logicalName,
                    descriptors,
                    PetInstalledSceneEffectActivation.AuthoredPlayOnAwakeOnly);
            Assert(
                conservative.DiscoveredCount == 4 &&
                conservative.EligibleCount == 1 &&
                conservative.Entries.Count == 1 &&
                conservative.Entries[0].StableKey == "auto" &&
                conservative.ExternallyTriggeredCount == 0,
                "the conservative particle plan stopped filtering authored " +
                "active/play-on-awake/renderer state");

            InstalledParticleSystemPlan optedIn =
                InstalledParticleEffectPlanner.BuildPlan(
                    logicalName,
                    descriptors,
                    PetInstalledSceneEffectActivation
                        .IncludeExternallyTriggered);
            Assert(
                optedIn.EligibleCount == 2 &&
                optedIn.Entries.Count == 2 &&
                optedIn.ExternallyTriggeredCount == 1 &&
                optedIn.Entries[1].StableKey == "manual" &&
                optedIn.Entries[1].ExternallyTriggered,
                "the explicit external-trigger particle plan did not select " +
                "the bounded manual system");

            var many = new List<InstalledParticleSystemDescriptor>();
            for (int index = 0; index < 14; index++)
            {
                many.Add(new InstalledParticleSystemDescriptor(
                    index.ToString("00"),
                    true,
                    true,
                    true,
                    1));
            }
            InstalledParticleSystemPlan systemCapped =
                InstalledParticleEffectPlanner.BuildPlan(
                    logicalName,
                    many,
                    PetInstalledSceneEffectActivation.AuthoredPlayOnAwakeOnly);
            Assert(
                systemCapped.Entries.Count ==
                    InstalledParticleEffectPlanner.MaximumParticleSystems &&
                systemCapped.SystemsTruncated,
                "the installed particle-system cap was not enforced");

            InstalledParticleSystemPlan particleCapped =
                InstalledParticleEffectPlanner.BuildPlan(
                    logicalName,
                    new[]
                    {
                        new InstalledParticleSystemDescriptor(
                            "large", true, true, true, 9000)
                    },
                    PetInstalledSceneEffectActivation.AuthoredPlayOnAwakeOnly);
            Assert(
                particleCapped.Entries[0].AppliedMaxParticles ==
                    InstalledParticleEffectPlanner.MaximumParticlesPerSystem &&
                particleCapped.ParticleBudgetTruncated,
                "the per-system particle cap was not enforced");
            uint first = InstalledParticleEffectPlanner.CreateStableSeed(
                logicalName,
                "auto");
            uint repeat = InstalledParticleEffectPlanner.CreateStableSeed(
                logicalName,
                "auto");
            uint other = InstalledParticleEffectPlanner.CreateStableSeed(
                logicalName,
                "manual");
            Assert(first != 0u && first == repeat && first != other,
                "installed particle seeds must be stable and path-specific");
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

        private static void ExpectInvalidDataFailure(
            Action action,
            string message)
        {
            try
            {
                action();
            }
            catch (InvalidDataException)
            {
                return;
            }
            throw new InvalidOperationException(message);
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

        private static void AssertApproximately(
            float expected,
            float actual,
            float tolerance,
            string name)
        {
            if (Mathf.Abs(expected - actual) > tolerance)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual +
                    " (tolerance " + tolerance + ").");
            }
        }
    }
}
