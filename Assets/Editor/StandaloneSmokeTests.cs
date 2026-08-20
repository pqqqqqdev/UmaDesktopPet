using UnityEngine;

namespace UmaDesktopPet.Standalone.Editor
{
    /// <summary>
    /// Single CI entry point for the standalone pet's dependency-free editor
    /// checks. Keeping one executeMethod avoids four separate Unity startups.
    /// </summary>
    public static class StandaloneSmokeTests
    {
        public static void RunAll()
        {
            GameInstallSmokeTests.Run();
            GameCompatibilityProbeSmokeTests.Run();
            DesktopPetPreferencesSmokeTests.Run();
            PetNeedsStateSmokeTests.Run();
            PetFocusStateSmokeTests.Run();
            PetStudyRewardServiceSmokeTests.Run();
            PetRecordingModeSmokeTests.Run();
            PetAttachmentRigSmokeTests.Run();
            DesktopWindowResizeSmokeTests.Run();
            Debug.Log("All standalone desktop-pet smoke tests passed.");
        }
    }
}
