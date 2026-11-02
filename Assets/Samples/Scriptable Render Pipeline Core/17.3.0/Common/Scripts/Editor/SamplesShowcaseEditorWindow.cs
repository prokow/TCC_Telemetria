using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;

namespace Samples.Scriptable_Render_Pipeline_Core._17._3._0.Common.Scripts.Editor
{
    public class SamplesWindow : EditorWindow
    {
        [InitializeOnLoadMethod]
        static void Init() 
        {
            EditorSceneManager.sceneOpened += SceneOpened;
        }


        static void SceneOpened(Scene scene, OpenSceneMode openSceneMode)
        {
            var currentShowcase = (SamplesShowcase)FindFirstObjectByType(typeof(SamplesShowcase));
            if(currentShowcase != null)
                Selection.activeGameObject = currentShowcase.gameObject;
        }



    }
}



