#if UNITY_EDITOR

namespace Samples.Scriptable_Render_Pipeline_Core._17._3._0.Common.Scripts.RequiredSettings
{ 
    public interface IRequiredSetting
    {
        public bool state { get; }
        public string name { get; }
        public string description { get; }
    }
}
#endif