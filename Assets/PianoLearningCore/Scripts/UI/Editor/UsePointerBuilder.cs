using UnityEditor;
using UnityEngine;

namespace PianoLearningCore
{
    /// <summary>
    /// Switches world-space menu input back to the laser <see cref="ControllerUIPointer"/>
    /// (point at a button, pull the trigger) and retires the thumbstick
    /// <see cref="ControllerMenuNavigator"/>. It re-enables every pointer in the scene and
    /// deactivates the navigator GameObject so it stops claiming the thumbstick — and so each
    /// pointer's "stand down if a navigator exists" guard (<see cref="ControllerUIPointer"/>.Start)
    /// passes and the pointers actually run.
    ///
    /// This is the inverse of Tools &gt; Piano Learning &gt; Create Menu Navigator. Run it once
    /// after the menus exist. Nothing is deleted — the navigator is only deactivated, so switching
    /// back is just re-running the navigator builder.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Use Controller Pointer
    /// </summary>
    public static class UsePointerBuilder
    {
        [MenuItem("Tools/Piano Learning/Use Controller Pointer")]
        public static void Create()
        {
            const string undoLabel = "Use Controller Pointer";

#pragma warning disable CS0618 // FindObjectsOfType works across every Unity version
            var pointers   = Object.FindObjectsOfType<ControllerUIPointer>(true);
            var navigators = Object.FindObjectsOfType<ControllerMenuNavigator>(true);

            // Re-enable every laser pointer. The navigator builder disabled them (enabled = false),
            // and a disabled component's Start() never runs — so it must be set back explicitly.
            int enabledNow = 0;
            foreach (var p in pointers)
            {
                if (!p.enabled)
                {
                    Undo.RecordObject(p, undoLabel);
                    p.enabled = true;
                    EditorUtility.SetDirty(p);
                    enabledNow++;
                }
            }

            // Deactivate the navigator so it stops driving the thumbstick and the pointers' guard passes.
            int deactivatedNow = 0;
            foreach (var nav in navigators)
            {
                if (nav.gameObject.activeSelf)
                {
                    Undo.RecordObject(nav.gameObject, undoLabel);
                    nav.gameObject.SetActive(false);
                    EditorUtility.SetDirty(nav.gameObject);
                    deactivatedNow++;
                }
            }
#pragma warning restore CS0618

            var scene = UnityEditor.SceneManagement.EditorSceneManager.GetActiveScene();
            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);

            // Report the FULL state, not just what changed, so "nothing changed" isn't mistaken for
            // "nothing found". FindObjectsOfType only searches OPEN scenes, so zero pointers almost
            // always means the menu scene isn't the one open.
            if (pointers.Length == 0)
            {
                Debug.LogWarning($"[UsePointerBuilder] Found NO ControllerUIPointer in the open scene(s) " +
                    $"(active scene: '{scene.name}'). Each menu canvas (SongSelectionCanvas / " +
                    $"ScoreBoardCanvas / PlaybackControlsCanvas) carries one, so either the scene with the " +
                    $"menu isn't open, or the pointers were removed. Open the scene that has your song menu " +
                    $"(e.g. KeyboardConfig.unity) and run this again.");
                return;
            }

            Debug.Log($"[UsePointerBuilder] Scene '{scene.name}': {pointers.Length} ControllerUIPointer(s) " +
                $"found — enabled {enabledNow} that were off (the rest were already on). " +
                $"{navigators.Length} MenuNavigator(s) found — deactivated {deactivatedNow} that were active. " +
                (navigators.Length == 0 ? "No navigator present. " : "") +
                "The laser pointer is now the menu input (point + trigger). Build to the headset to test — " +
                "aim at a song's Practice/Assessment button and pull the trigger.");
        }
    }
}
