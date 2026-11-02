using Plugins.CodeStage.AdvancedFPSCounter.Runtime.Scripts.Utils;
using UnityEditor;
using UnityEngine;

#if UNITY_EDITOR
namespace Plugins.CodeStage.AdvancedFPSCounter.Editor.Scripts
{
	[CustomEditor(typeof(AFPSRenderRecorder))]
	[CanEditMultipleObjects()]
	public class AFPSRenderRecorderEditor : UnityEditor.Editor
	{
		public override void OnInspectorGUI()
		{
			DrawDefaultInspector();
			EditorUIUtils.SetupStyles();
			GUILayout.Label("This component is used by <b>Advanced FPS Counter</b> to measure camera <b>Render Time</b>.", EditorUIUtils.richMiniLabel);
		}
	}
}
#endif