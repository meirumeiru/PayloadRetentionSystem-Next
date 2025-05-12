using UnityEngine;

using PayloadRetentionSystemNext.Module;

namespace PayloadRetentionSystemNext.Utility
{
	[KSPAddon(KSPAddon.Startup.EditorAny, false)]
	public class EditorEventLogic : MonoBehaviour
	{
		public static EditorEventLogic Instance;

		private void Awake()
		{
			if(HighLogic.LoadedSceneIsEditor)
			{
				GameEvents.onEditorPartEvent.Add(OnEditorPartEvent);
				Instance = this;
			}
			else
				Instance = null;
		}

		private void OnDestroy()
		{
			GameEvents.onEditorPartEvent.Remove(OnEditorPartEvent);
		}

		////////////////////
		// Callback Functions
		
		public void OnEditorPartEvent(ConstructionEventType evt, Part part)
		{
			switch(evt)
			{
			case ConstructionEventType.PartAttached:
				{
					ModuleTrunnionPins p = part.GetComponent<ModuleTrunnionPins>();
					ModuleTrunnionLatches l = part.parent.GetComponent<ModuleTrunnionLatches>();

					if(p && l)
					{
						l.length = Mathf.Abs(p.length);
						l.width = p.width;

						l.UpdateDimension();
					}
					else if(p)
					{
						ModuleTrunnionPins pp = part.parent.GetComponent<ModuleTrunnionPins>();

						if(pp)
						{
							if((p.portMode < 2) && (pp.portMode < 2))
								pp.SetCompanion(p);
						}
					}
				}
				break;

			case ConstructionEventType.PartDetached:
				{
					ModuleTrunnionPins pp = part.GetComponent<ModuleTrunnionPins>();

					if(pp)
						pp.ClearCompanion();
				}
				break;
			}
		}
	}
}
