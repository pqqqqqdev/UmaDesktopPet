using System;
using UmaDesktopPet.Standalone.Runtime;
using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Dependency-free checks for semantic slot resolution and fail-closed prop
    /// attachment. Run with Unity's -executeMethod option when wired into the
    /// project's aggregate smoke suite.
    /// </summary>
    public static class PetAttachmentRigSmokeTests
    {
        private const string LeftHandPath =
            "Position/Hip/Waist/Chest/Shoulder_L/Arm_L/Elbow_L/" +
            "Wrist_L/Hand_Attach_L";
        private const string RightHandPath =
            "Position/Hip/Waist/Chest/Shoulder_R/Arm_R/Elbow_R/" +
            "Wrist_R/Hand_Attach_R";
        private const string HeadPath =
            "Position/Hip/Waist/Chest/Neck/Head";

        public static void Run()
        {
            RunExactHandsTest();
            RunSyntheticCalibratedMouthTest();
            RunUniqueNameFallbackTest();
            RunMissingSlotFailClosedTest();
            RunWorldSocketCleanupTest();
            Debug.Log("Pet attachment rig smoke tests passed.");
        }

        private static void RunExactHandsTest()
        {
            GameObject visualFrame = null;
            GameObject attachedProp = null;
            try
            {
                Transform characterRoot;
                visualFrame = CreateCharacterFixture(out characterRoot);
                Transform exactLeft = CreatePath(characterRoot, LeftHandPath);
                Transform exactRight = CreatePath(characterRoot, RightHandPath);

                // Exact paths must win even when fallback names are ambiguous.
                CreateNamedChild(characterRoot, "Hand_Attach_L");
                CreateNamedChild(characterRoot, "Hand_Attach_R");

                PetAttachmentRig rig =
                    characterRoot.gameObject.AddComponent<PetAttachmentRig>();
                rig.Initialize(
                    characterRoot,
                    visualFrame.transform,
                    PetAttachmentProfileCatalog.OguriCap);

                Transform leftSocket;
                Transform rightSocket;
                Transform mouthSocket;
                Transform worldSocket;
                Transform rootSocket;
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.LeftHand, out leftSocket),
                    "left-hand slot should resolve");
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.RightHand, out rightSocket),
                    "right-hand slot should resolve");
                Assert(
                    !rig.TryGetSlot(PetAttachmentSlots.Mouth, out mouthSocket),
                    "uncalibrated Oguri mouth slot must stay unavailable");
                Assert(mouthSocket == null, "unavailable mouth socket should be null");
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.World, out worldSocket),
                    "world slot should resolve");
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.PetRoot, out rootSocket),
                    "pet-root slot should resolve");

                AssertEqual(exactLeft, leftSocket.parent, "exact left-hand parent");
                AssertEqual(exactRight, rightSocket.parent, "exact right-hand parent");
                AssertEqual(
                    visualFrame.transform,
                    worldSocket.parent,
                    "world socket parent");
                AssertEqual(characterRoot, rootSocket.parent, "pet-root socket parent");
                PetAttachmentSlotProfile mouthProfile;
                Assert(
                    !PetAttachmentProfileCatalog.OguriCap.TryGetSlotProfile(
                        PetAttachmentSlots.Mouth,
                        out mouthProfile),
                    "Oguri must not publish an uncalibrated mouth profile");
                Assert(mouthProfile == null, "missing Oguri mouth profile should be null");

                AssertMatrixNear(
                    characterRoot.localToWorldMatrix,
                    worldSocket.localToWorldMatrix,
                    "world socket character matrix");

                attachedProp = new GameObject("Successfully attached prop");
                attachedProp.transform.localPosition = new Vector3(3.0f, -2.0f, 1.0f);
                attachedProp.transform.localRotation = Quaternion.Euler(20.0f, 30.0f, 40.0f);
                attachedProp.transform.localScale = new Vector3(2.0f, 3.0f, 4.0f);
                Assert(
                    rig.TryAttach(attachedProp.transform, PetAttachmentSlots.LeftHand),
                    "attachment to a resolved slot should succeed");
                AssertEqual(leftSocket, attachedProp.transform.parent, "attached prop parent");
                AssertVectorNear(Vector3.zero, attachedProp.transform.localPosition, "attached prop position");
                AssertQuaternionNear(
                    Quaternion.identity,
                    attachedProp.transform.localRotation,
                    "attached prop rotation");
                AssertVectorNear(Vector3.one, attachedProp.transform.localScale, "attached prop scale");

                Vector3 normalized = new Vector3(0.25f, -0.40f, 0.10f);
                AssertVectorNear(
                    normalized,
                    rig.ToCharacterHeightUnits(
                        rig.FromCharacterHeightUnits(normalized)),
                    "normalized coordinate round trip");
                AssertNear(4.0f, rig.CharacterHeight, "character height");
            }
            finally
            {
                if (attachedProp != null)
                {
                    UnityEngine.Object.DestroyImmediate(attachedProp);
                }
                if (visualFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualFrame);
                }
            }
        }

        private static void RunSyntheticCalibratedMouthTest()
        {
            GameObject visualFrame = null;
            try
            {
                Transform characterRoot;
                visualFrame = CreateCharacterFixture(out characterRoot);
                Transform head = CreatePath(characterRoot, HeadPath);
                Vector3 normalizedOffset = new Vector3(0.01f, -0.04f, 0.03f);
                Vector3 rotationOffset = new Vector3(5.0f, 10.0f, -3.0f);
                var profile = new PetAttachmentProfile(
                    "synthetic-mouth-test",
                    new[]
                    {
                        new PetAttachmentSlotProfile(
                            PetAttachmentSlots.Mouth,
                            HeadPath,
                            "Head",
                            normalizedOffset,
                            rotationOffset)
                    });

                PetAttachmentRig rig =
                    characterRoot.gameObject.AddComponent<PetAttachmentRig>();
                rig.Initialize(characterRoot, visualFrame.transform, profile);

                Transform mouthSocket;
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.Mouth, out mouthSocket),
                    "a synthetically calibrated mouth slot should resolve");
                AssertEqual(head, mouthSocket.parent, "synthetic mouth head parent");
                AssertVectorNear(
                    normalizedOffset * rig.CharacterHeight,
                    mouthSocket.localPosition,
                    "synthetic height-normalized mouth offset");
                AssertQuaternionNear(
                    Quaternion.Euler(rotationOffset),
                    mouthSocket.localRotation,
                    "synthetic mouth rotation");
            }
            finally
            {
                if (visualFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualFrame);
                }
            }
        }

        private static void RunUniqueNameFallbackTest()
        {
            GameObject visualFrame = null;
            try
            {
                Transform characterRoot;
                visualFrame = CreateCharacterFixture(out characterRoot);
                Transform uniqueHand = CreateNamedChild(
                    characterRoot,
                    "Only_Fallback_Hand");
                var profile = new PetAttachmentProfile(
                    "fallback-test",
                    new[]
                    {
                        new PetAttachmentSlotProfile(
                            PetAttachmentSlots.RightHand,
                            "Missing/Exact/Path",
                            "Only_Fallback_Hand",
                            Vector3.zero,
                            Vector3.zero)
                    });

                PetAttachmentRig rig =
                    characterRoot.gameObject.AddComponent<PetAttachmentRig>();
                rig.Initialize(characterRoot, visualFrame.transform, profile);

                Transform socket;
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.RightHand, out socket),
                    "a unique-name fallback should resolve");
                AssertEqual(uniqueHand, socket.parent, "unique fallback parent");
            }
            finally
            {
                if (visualFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualFrame);
                }
            }
        }

        private static void RunMissingSlotFailClosedTest()
        {
            GameObject visualFrame = null;
            GameObject prop = null;
            try
            {
                Transform characterRoot;
                visualFrame = CreateCharacterFixture(out characterRoot);
                CreateNamedChild(characterRoot, "Ambiguous_Hand");
                Transform duplicateParent = CreateNamedChild(
                    characterRoot,
                    "Duplicate Parent");
                CreateNamedChild(duplicateParent, "Ambiguous_Hand");

                var profile = new PetAttachmentProfile(
                    "missing-test",
                    new[]
                    {
                        new PetAttachmentSlotProfile(
                            PetAttachmentSlots.RightHand,
                            "Missing/Exact/Path",
                            "Ambiguous_Hand",
                            Vector3.zero,
                            Vector3.zero)
                    });
                PetAttachmentRig rig =
                    characterRoot.gameObject.AddComponent<PetAttachmentRig>();
                rig.Initialize(characterRoot, visualFrame.transform, profile);

                Transform missingSocket;
                Assert(
                    !rig.TryGetSlot(
                        PetAttachmentSlots.RightHand,
                        out missingSocket),
                    "an ambiguous fallback must not resolve");
                Assert(missingSocket == null, "missing socket result should be null");

                prop = new GameObject("Rejected prop");
                Vector3 originalPosition = new Vector3(17.0f, -11.0f, 4.0f);
                prop.transform.position = originalPosition;
                Assert(
                    !rig.TryAttach(prop.transform, PetAttachmentSlots.RightHand),
                    "attachment to a missing slot must fail");
                Assert(!prop.activeSelf, "a rejected prop must be hidden");
                Assert(
                    prop.transform.parent == null,
                    "a rejected prop must remain unparented");
                AssertVectorNear(
                    originalPosition,
                    prop.transform.position,
                    "rejected prop must not move to an origin fallback");
            }
            finally
            {
                if (prop != null)
                {
                    UnityEngine.Object.DestroyImmediate(prop);
                }
                if (visualFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualFrame);
                }
            }
        }

        private static void RunWorldSocketCleanupTest()
        {
            GameObject visualFrame = null;
            try
            {
                Transform characterRoot;
                visualFrame = CreateCharacterFixture(out characterRoot);
                var profile = new PetAttachmentProfile(
                    "world-cleanup-test",
                    new PetAttachmentSlotProfile[0]);
                PetAttachmentRig rig =
                    characterRoot.gameObject.AddComponent<PetAttachmentRig>();
                rig.Initialize(characterRoot, visualFrame.transform, profile);

                Transform worldSocket;
                Assert(
                    rig.TryGetSlot(PetAttachmentSlots.World, out worldSocket),
                    "world socket should exist before character cleanup");
                AssertEqual(
                    visualFrame.transform,
                    worldSocket.parent,
                    "world cleanup socket parent");

                rig.ReleaseResources();
                UnityEngine.Object.DestroyImmediate(characterRoot.gameObject);

                Assert(visualFrame != null, "visual frame should survive character cleanup");
                Assert(worldSocket == null, "world socket should be destroyed with its rig owner");
                Assert(
                    visualFrame.transform.Find("Pet Prop Slot - " + PetAttachmentSlots.World) == null,
                    "visual frame must not retain an orphaned world socket");
            }
            finally
            {
                if (visualFrame != null)
                {
                    UnityEngine.Object.DestroyImmediate(visualFrame);
                }
            }
        }

        private static GameObject CreateCharacterFixture(
            out Transform characterRoot)
        {
            var visualFrame = new GameObject("Attachment smoke visual frame");
            visualFrame.transform.position = new Vector3(6.0f, -2.0f, 11.0f);
            visualFrame.transform.rotation = Quaternion.Euler(0.0f, 15.0f, 0.0f);
            visualFrame.transform.localScale = new Vector3(1.1f, 1.1f, 1.1f);

            var character = new GameObject("Attachment smoke character");
            characterRoot = character.transform;
            characterRoot.SetParent(visualFrame.transform, false);
            characterRoot.localPosition = new Vector3(0.75f, 1.25f, -0.50f);
            characterRoot.localRotation = Quaternion.Euler(0.0f, -8.0f, 0.0f);
            characterRoot.localScale = new Vector3(0.9f, 0.9f, 0.9f);

            GameObject boundsObject = GameObject.CreatePrimitive(PrimitiveType.Cube);
            boundsObject.name = "Character Bounds Mesh";
            boundsObject.transform.SetParent(characterRoot, false);
            boundsObject.transform.localPosition = new Vector3(0.0f, 2.0f, 0.0f);
            boundsObject.transform.localScale = new Vector3(2.0f, 4.0f, 1.0f);
            return visualFrame;
        }

        private static Transform CreatePath(Transform root, string path)
        {
            Transform current = root;
            string[] names = path.Split('/');
            for (int index = 0; index < names.Length; index++)
            {
                Transform child = current.Find(names[index]);
                if (child == null)
                {
                    child = CreateNamedChild(current, names[index]);
                }
                current = child;
            }
            return current;
        }

        private static Transform CreateNamedChild(Transform parent, string name)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition)
            {
                throw new InvalidOperationException(message);
            }
        }

        private static void AssertNear(float expected, float actual, string name)
        {
            if (Mathf.Abs(expected - actual) > 0.001f)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected + " but was " + actual + ".");
            }
        }

        private static void AssertVectorNear(
            Vector3 expected,
            Vector3 actual,
            string name)
        {
            if (Vector3.SqrMagnitude(expected - actual) > 0.000001f)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected.ToString("F4") +
                    " but was " + actual.ToString("F4") + ".");
            }
        }

        private static void AssertQuaternionNear(
            Quaternion expected,
            Quaternion actual,
            string name)
        {
            if (Quaternion.Angle(expected, actual) > 0.001f)
            {
                throw new InvalidOperationException(
                    name + " expected " + expected.eulerAngles.ToString("F4") +
                    " but was " + actual.eulerAngles.ToString("F4") + ".");
            }
        }

        private static void AssertMatrixNear(
            Matrix4x4 expected,
            Matrix4x4 actual,
            string name)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    float expectedValue = expected[row, column];
                    float actualValue = actual[row, column];
                    if (Mathf.Abs(expectedValue - actualValue) > 0.0001f)
                    {
                        throw new InvalidOperationException(
                            name + " differs at [" + row + "," + column + "]: " +
                            "expected " + expectedValue + " but was " + actualValue + ".");
                    }
                }
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
