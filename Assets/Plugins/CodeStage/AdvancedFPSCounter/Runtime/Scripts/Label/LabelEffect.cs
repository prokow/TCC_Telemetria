#region copyright
//-------------------------------------------------------
// Copyright (C) Dmitriy Yukhanov [https://codestage.net]
//-------------------------------------------------------
#endregion

using UnityEngine;

namespace Plugins.CodeStage.AdvancedFPSCounter.Runtime.Scripts.Label
{
	internal class LabelEffect
	{
		public bool enabled;
		public Color color;
		public Vector2 distance;
		public int padding;

		internal LabelEffect(bool enabled, Color color, int padding) : this(enabled, color, Vector2.zero)
		{
			this.padding = padding;
		}

		internal LabelEffect(bool enabled, Color color, Vector2 distance)
		{
			this.enabled = enabled;
			this.color = color;
			this.distance = distance;
		}
	}
}