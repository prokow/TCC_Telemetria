#region copyright
// --------------------------------------------------------------
//  Copyright (C) Dmitriy Yukhanov - focus [http://codestage.net]
// --------------------------------------------------------------
#endregion

namespace Plugins.CodeStage.AdvancedFPSCounter.Runtime.Scripts.CountersData.Abstract
{
	public abstract class StaticCounterData : BaseCounterData
	{
		internal override void Activate()
		{
			base.Activate();
			
			if (inited)
				UpdateValue();
		}
	}
}