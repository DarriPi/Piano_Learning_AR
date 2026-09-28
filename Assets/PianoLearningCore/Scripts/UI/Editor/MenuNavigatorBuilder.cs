using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

namespace PianoLearningCore
{
    /// <summary>
    /// Creates the single <see cref="ControllerMenuNavigator"/> that drives every world-space menu
    /// with the thumbstick + trigger, and disables any old <see cref="ControllerUIPointer"/> lasers in
    /// the scene so they don't draw or compete. Run once after the menus exist.
    ///
    /// Menu: Tools &gt; Piano Learning &gt; Create Menu Navigator
    /// </summary>
    public static class MenuNavigatorBuilder
    {
        [MenuItem("Tools/Piano Learning/Create Menu Navigator")]
        public static void Create()
        {
            const string undoLabel = "Create Menu Navigator";

#pragma warning disable CS0618 // FindObjectOfType(s) works across every Unity version
            if (Object.FindObjectOfType<EventSystem>() == null)
            {
                var es = new GameObject("EventSystem", typeof(EventSystem), typeof(StandaloneInputModule));
                Undo.RegisterCreatedObjectUndo(es, undoLabel);
            }

            var navigator = Object.FindObjectOfType<ControllerMenuNavigator>();
            if (navigator == null)
            {
                var go = new GameObject("MenuNavigator", typeof(ControllerMenuNavigator));
                Undo.RegisterCreatedObjectUndo(go, undoLabel);
                navigator = go.GetComponent<ControllerMenuNavigator>();
                Selection.activeGameObject = go;
            }

            // Retire the old laser pointers so they don't draw or fight the navigator.
            int disabled = 0;
            foreach (var p in Object.FindObjectsOfType<ControllerUIPointer>(true))
            {
                if (p.enabled)
                {
                    Undo.RecordObject(p, undoLabel);
                    p.enabled = false;
                    EditorUtility.SetDirty(p);
                    disabled++;
                }
            }
#pragma warning restore CS0618

            UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(navigator.gameObject.scene);
            Debug.Log($"[MenuNavigatorBuilder] Menu navigator ready (thumbstick = move highlight, " +
                      $"trigger = select). Disabled {disabled} old ControllerUIPointer laser(s). " +
                      $"HelpPanel turns the navigator on/off as Help closes/opens.");
        }
    }
}
