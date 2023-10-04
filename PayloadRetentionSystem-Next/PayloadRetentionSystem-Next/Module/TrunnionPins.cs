using System;
using System.Collections.Generic;
using System.Collections;

using UnityEngine;

using DockingFunctions;

namespace PayloadRetentionSystemNext.Module
{
	// FEHLER, Crossfeed noch einrichten... und halt umbauen auf FSM? ... ja, zum Spass... shit ey

	// FEHLER, wir arbeiten bei den Events nie mit "OnCheckCondition" sondern lösen alle manuell aus... kann man sich fragen, ob das gut ist, aber so lange der Event nur von einem Zustand her kommen kann, spielt das wie keine Rolle

	public class ModuleTrunnionPins : PartModule, IDockable, ITargetable, IModuleInfo
	{
		// Settings

		[KSPField(isPersistant = false), SerializeField]
		public string nodeTransformName = "TrunnionPinsNode";

		[KSPField(isPersistant = false), SerializeField]
		public string controlTransformName = "";

		[KSPField(isPersistant = false), SerializeField]
		public Vector3 dockingOrientation = Vector3.right; // defines the direction of the docking port (when docked at a 0° angle, these local vectors of two ports point into the same direction)

		[KSPField(isPersistant = false), SerializeField]
		public int snapCount = 2;


		[KSPField(isPersistant = false)]
		public bool gendered = true;

		[KSPField(isPersistant = false)]
		public bool genderFemale = false;

		[KSPField(isPersistant = false)]
		public string nodeType = "Trunnion";

		[KSPField(isPersistant = false)]
		public float breakingForce = 10f;

		[KSPField(isPersistant = false)]
		public float breakingTorque = 10f;

		[KSPField(isPersistant = false)]
		public string nodeName = "";				// FEHLER, mal sehen wozu wir den dann nutzen könnten


		// Docking and Status

		public BaseEvent evtSetAsTarget;
		public BaseEvent evtUnsetTarget;

		public Transform nodeTransform;
		public Transform controlTransform;

//		public Transform portTransform; // FEHLER, neue Idee -> und, wozu sind die anderen da oben eigentlich gut?

		public KerbalFSM fsm;

		public KFSMState st_passive;

		public KFSMState st_approaching_passive;

		public KFSMState st_latched_passive;

		public KFSMState st_docked;			// docked or docked_to_same_vessel
		public KFSMState st_preattached;

		public KFSMState st_disabled;


		public KFSMEvent on_approach_passive;
		public KFSMEvent on_distance_passive;

		public KFSMEvent on_latch_passive;

		public KFSMEvent on_release_passive;

		public KFSMEvent on_dock_passive;
		public KFSMEvent on_undock_passive;

		public KFSMEvent on_enable;
		public KFSMEvent on_disable;

		// Capturing / Docking

		public ModuleTrunnionLatches otherPort;
		public uint dockedPartUId;

		public DockedVesselInfo vesselInfo;

		////////////////////////////////////////
		// Constructor

		public ModuleTrunnionPins()
		{
		}

		////////////////////////////////////////
		// Callbacks

		public override void OnAwake()
		{
			part.dockingPorts.AddUnique(this);
		}

		public override void OnLoad(ConfigNode node)
		{
			base.OnLoad(node);

			if((part.partInfo != null) && (part.partInfo.partPrefab != null))
			{
			}
			else // I assume, that I'm the prefab then
			{
				Transform Plate000 = KSPUtil.FindInPartModel(transform, "Plate.000");
				Transform Plate001 = KSPUtil.FindInPartModel(transform, "Plate.001");
				Transform Plate002 = KSPUtil.FindInPartModel(transform, "Plate.002");
				Transform Plate003 = KSPUtil.FindInPartModel(transform, "Plate.003");

				Vector3 Pos000 = Plate000.localPosition;
				Pos000.x = length;
				Pos000.z = -1.231f - width;
				Plate000.localPosition = Pos000;

				Vector3 Pos001 = Plate001.localPosition;
				Pos001.x = -length;
				Pos001.z = -1.231f - width;
				Plate001.localPosition = Pos001;

				Vector3 Pos002 = Plate002.localPosition;
				Pos002.x = length;
				Pos002.z = 1.231f + width;
				Plate002.localPosition = Pos002;

				Vector3 Pos003 = Plate003.localPosition;
				Pos003.x = -length;
				Pos003.z = 1.231f + width;
				Plate003.localPosition = Pos003;
			}

			if(node.HasValue("state"))
				DockStatus = node.GetValue("state");
			else
				DockStatus = "Ready";

			if(node.HasValue("dockUId"))
				dockedPartUId = uint.Parse(node.GetValue("dockUId"));

			if(node.HasNode("DOCKEDVESSEL"))
			{
				vesselInfo = new DockedVesselInfo();
				vesselInfo.Load(node.GetNode("DOCKEDVESSEL"));
			}
		}

		public override void OnSave(ConfigNode node)
		{
			base.OnSave(node);

			node.AddValue("state", (string)(((fsm != null) && (fsm.Started)) ? fsm.currentStateName : DockStatus));

			node.AddValue("dockUId", dockedPartUId);

			if(vesselInfo != null)
				vesselInfo.Save(node.AddNode("DOCKEDVESSEL"));
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			evtSetAsTarget = base.Events["SetAsTarget"];
			evtUnsetTarget = base.Events["UnsetTarget"];

			nodeTransform = base.part.FindModelTransform(nodeTransformName);
			if(!nodeTransform)
			{
				Debug.LogWarning("[Docking Node Module]: WARNING - No node transform found with name " + nodeTransformName, base.part.gameObject);
				return;
			}
			if(controlTransformName == string.Empty)
				controlTransform = base.part.transform;
			else
			{
				controlTransform = base.part.FindModelTransform(controlTransformName);
				if(!controlTransform)
				{
					Debug.LogWarning("[Docking Node Module]: WARNING - No control transform found with name " + controlTransformName, base.part.gameObject);
					controlTransform = base.part.transform;
				}
			}

//			portTransform = part.FindAttachNode("TrunnionPinsNode").nodeTransform;

// FEHLER, Test
/*fake = new GameObject();
fake.transform.position = portTransform.position;
fake.transform.rotation = Quaternion.AngleAxis(180f, -portTransform.right) * portTransform.rotation;
fake.transform.parent = portTransform;
fake.SetActive(true);
fake_nodeTransform = fake.transform;
*/
			if(state == StartState.Editor)
			{
				Fields["length"].OnValueModified += onChanged_length;
				Fields["width"].OnValueModified += onChanged_width;
				Fields["offsetY"].OnValueModified += onChanged_offsetY;
				Fields["offsetX"].OnValueModified += onChanged_offsetX;
				Fields["offsetZ"].OnValueModified += onChanged_offsetZ;
			}

			onChanged_length(null);
			onChanged_width(null);
			onChanged_offsetY(null);
			onChanged_offsetX(null);
			onChanged_offsetZ(null);

			StartCoroutine(WaitAndInitialize(state));

			StartCoroutine(WaitAndInitializeDockingNodeFix());
		}

		// FEHLER, ist 'n Quickfix, solange der blöde Port noch drüber hängt im Part...
		public IEnumerator WaitAndInitializeDockingNodeFix()
		{
			ModuleDockingNode DockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if(DockingNode)
			{
				while((DockingNode.fsm == null) || (!DockingNode.fsm.Started))
					yield return null;

				DockingNode.fsm.RunEvent(DockingNode.on_disable);
			}
		}

		public IEnumerator WaitAndInitialize(StartState st)
		{
			yield return null;

			Events["TogglePort"].active = false;

//			Events["EnableXFeed"].active = !crossfeed;
//			Events["DisableXFeed"].active = crossfeed;

			SetupFSM();

			fsm.StartFSM(DockStatus);

// FEHLER, ich versuch was -> geht, ist nur fraglich, wieso das nötig ist
if(st == StartState.Editor)
			{
yield return new WaitForFixedUpdate();
yield return new WaitForFixedUpdate();
yield return new WaitForFixedUpdate();
MoveNode();
			}
		}

		public void OnDestroy()
		{
			Fields["length"].OnValueModified -= onChanged_length;
			Fields["width"].OnValueModified -= onChanged_width;
			Fields["offsetY"].OnValueModified -= onChanged_offsetY;
			Fields["offsetX"].OnValueModified -= onChanged_offsetX;
			Fields["offsetZ"].OnValueModified -= onChanged_offsetZ;
		}

		////////////////////////////////////////
		// Functions

		public void SetupFSM()
		{
			fsm = new KerbalFSM();

			st_passive = new KFSMState("Ready");
			st_passive.OnEnter = delegate(KFSMState from)
			{
				otherPort = null;
				dockedPartUId = 0;

				Events["TogglePort"].guiName = "Deactivate Trunnion Pins";
				Events["TogglePort"].active = true;
			};
			st_passive.OnFixedUpdate = delegate
			{
			};
			st_passive.OnLeave = delegate(KFSMState to)
			{
				if(to != st_disabled)
				{
					Events["TogglePort"].active = false;
				}
			};
			fsm.AddState(st_passive);

			st_approaching_passive = new KFSMState("Approaching");
			st_approaching_passive.OnEnter = delegate(KFSMState from)
			{
			};
			st_approaching_passive.OnFixedUpdate = delegate
			{
			};
			st_approaching_passive.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_approaching_passive);

			st_latched_passive = new KFSMState("Latched");
			st_latched_passive.OnEnter = delegate(KFSMState from)
			{
			};
			st_latched_passive.OnFixedUpdate = delegate
			{
			};
			st_latched_passive.OnLeave = delegate(KFSMState to)
			{
				if(to == st_passive)
				{
					otherPort = null;
					dockedPartUId = 0;
				}
			};
			fsm.AddState(st_latched_passive);

			st_docked = new KFSMState("Docked");
			st_docked.OnEnter = delegate(KFSMState from)
			{
			};
			st_docked.OnFixedUpdate = delegate
			{
			};
			st_docked.OnLeave = delegate(KFSMState to)
			{
				if(to == st_passive)
				{
					otherPort = null;
					dockedPartUId = 0;
				}
			};
			fsm.AddState(st_docked);

			st_preattached = new KFSMState("Attached");
			st_preattached.OnEnter = delegate(KFSMState from)
			{
			};
			st_preattached.OnFixedUpdate = delegate
			{
			};
			st_preattached.OnLeave = delegate(KFSMState to)
			{
				otherPort = null;
				dockedPartUId = 0;
			};
			fsm.AddState(st_preattached);

			st_disabled = new KFSMState("Inactive");
			st_disabled.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].guiName = "Activate Trunnion Pins";
				Events["TogglePort"].active = true;
			};
			st_disabled.OnFixedUpdate = delegate
			{
			};
			st_disabled.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_disabled);


			on_approach_passive = new KFSMEvent("Approaching");
			on_approach_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_approach_passive.GoToStateOnEvent = st_approaching_passive;
			fsm.AddEvent(on_approach_passive, st_passive);

			on_distance_passive = new KFSMEvent("Distancing");
			on_distance_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_distance_passive.GoToStateOnEvent = st_passive;
			fsm.AddEvent(on_distance_passive, st_approaching_passive);

// FEHLER FEHLER, hier bin ich noch unsicher, ob das alles stimmt -> hab den "Captured" rausgenommen, aber weiss nicht, ob alles passt jetzt
			on_latch_passive = new KFSMEvent("Latched");
			on_latch_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_latch_passive.GoToStateOnEvent = st_latched_passive;
			fsm.AddEvent(on_latch_passive, st_approaching_passive, st_passive);

			on_release_passive = new KFSMEvent("Released");
			on_release_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_release_passive.GoToStateOnEvent = st_passive;
			fsm.AddEvent(on_release_passive, st_latched_passive);


			on_dock_passive = new KFSMEvent("Dock");
			on_dock_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_dock_passive.GoToStateOnEvent = st_docked;
			fsm.AddEvent(on_dock_passive, st_latched_passive);

			on_undock_passive = new KFSMEvent("Undock");
			on_undock_passive.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_undock_passive.GoToStateOnEvent = st_latched_passive;
			fsm.AddEvent(on_undock_passive, st_docked, st_preattached);


			on_enable = new KFSMEvent("Enable");
			on_enable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_enable.GoToStateOnEvent = st_passive;
			fsm.AddEvent(on_enable, st_disabled);

			on_disable = new KFSMEvent("Disable");
			on_disable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_disable.GoToStateOnEvent = st_disabled;
			fsm.AddEvent(on_disable, st_passive);
		}

		////////////////////////////////////////
		// Update-Functions

		public void FixedUpdate()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
					fsm.FixedUpdateFSM();

				}
			}
		}

		public void Update()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
				{
					fsm.UpdateFSM();
					DockStatus = fsm.currentStateName;
				}

				if(FlightGlobals.fetch.VesselTarget == (ITargetable)this)
				{
					evtSetAsTarget.active = false;
					evtUnsetTarget.active = true;

					if(FlightGlobals.ActiveVessel == vessel)
						FlightGlobals.fetch.SetVesselTarget(null);
					else if((FlightGlobals.ActiveVessel.transform.position - nodeTransform.position).sqrMagnitude > 40000f)
						FlightGlobals.fetch.SetVesselTarget(vessel);
				}
				else
				{
					evtSetAsTarget.active = true;
					evtUnsetTarget.active = false;
				}
			
				}
			}
		}

		public void LateUpdate()
		{
			if(HighLogic.LoadedSceneIsFlight)
			{
				if(vessel && !vessel.packed)
				{

				if((fsm != null) && fsm.Started)
					fsm.LateUpdateFSM();

				}
			}
		}

		////////////////////////////////////////
		// Settings

		private bool Is45(Transform Pins)
		{
			return Pins.name.Contains("45");
		}

	//	private bool Is45()
	//	{
	//		Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;
	//		return Pins.name.Contains("45");
	//	}

// FEHLER, erste Idee
		private void MoveNode()
		{
			part.UpdateAttachNodes(); // FEHLER, sehen ob's geht... wenn ja -> dessen Code nutzen? um nur unseren zu aktualisieren?
			return;


			int i = 0;
			while(part.attachNodes[i].id != "TrunnionPinsNode") ++i;

	//		int j = 0;
	//		while(part.partInfo.partPrefab.attachNodes[j].id != "TrunnionPinsNode") ++j;

			AttachNode node = part.attachNodes[i];
	//		AttachNode baseNode = part.partInfo.partPrefab.attachNodes[j];

	//		node.position = baseNode.position;
	//		node.originalPosition = baseNode.originalPosition;

			Transform nodeTransform = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode");

			node.position = nodeTransform.position;
			node.originalPosition = nodeTransform.position;
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Distance (x)", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = 0.6f, maxValue = 8f),
			UI_FloatRange(minValue = 0.6f, maxValue = 8f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float length = 2f;

		private void onChanged_length(object o)
		{
			Transform Plate000 = KSPUtil.FindInPartModel(transform, "Plate.000");
			Transform Plate001 = KSPUtil.FindInPartModel(transform, "Plate.001");
			Transform Plate002 = KSPUtil.FindInPartModel(transform, "Plate.002");
			Transform Plate003 = KSPUtil.FindInPartModel(transform, "Plate.003");

			Vector3 Pos000 = Plate000.localPosition;
			Pos000.x = length * 0.5f;
			Plate000.localPosition = Pos000;

			Vector3 Pos001 = Plate001.localPosition;
			Pos001.x = -length * 0.5f;
			Plate001.localPosition = Pos001;

			Vector3 Pos002 = Plate002.localPosition;
			Pos002.x = length * 0.5f;
			Plate002.localPosition = Pos002;

			Vector3 Pos003 = Plate003.localPosition;
			Pos003.x = -length * 0.5f;
			Plate003.localPosition = Pos003;
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Distance (z)", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = -0.6f, maxValue = 0.6f),
			UI_FloatRange(minValue = -0.6f, maxValue = 0.6f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float width = 0f;

		private void onChanged_width(object o)
		{
			Transform Plate000 = KSPUtil.FindInPartModel(transform, "Plate.000");
			Transform Plate001 = KSPUtil.FindInPartModel(transform, "Plate.001");
			Transform Plate002 = KSPUtil.FindInPartModel(transform, "Plate.002");
			Transform Plate003 = KSPUtil.FindInPartModel(transform, "Plate.003");

			bool is45 = Is45(Plate000.parent);

			Vector3 Pos000 = Plate000.localPosition;
			if(is45)
			{
				Pos000.y = 1.0071f + (0.7071f * width * 0.5f);
				Pos000.z = -0.7338f - (0.7071f * width * 0.5f);
			}
			else
				Pos000.z = -1.231f - width * 0.5f;
			Plate000.localPosition = Pos000;

			Vector3 Pos001 = Plate001.localPosition;
			if(is45)
			{
				Pos001.y = 1.0071f + (0.7071f * width * 0.5f);
				Pos001.z = -0.7338f - (0.7071f * width * 0.5f);
			}
			else
				Pos001.z = -1.231f - width * 0.5f;
			Plate001.localPosition = Pos001;

			Vector3 Pos002 = Plate002.localPosition;
			if(is45)
			{
				Pos002.y = -0.7338f - (0.7071f * width * 0.5f);
				Pos002.z = 1.0071f + (0.7071f * width * 0.5f);
			}
			else
				Pos002.z = 1.231f + width * 0.5f;
			Plate002.localPosition = Pos002;

			Vector3 Pos003 = Plate003.localPosition;
			if(is45)
			{
				Pos003.y = -0.7338f - (0.7071f * width * 0.5f);
				Pos003.z = 1.0071f + (0.7071f * width * 0.5f);
			}
			else
				Pos003.z = 1.231f + width * 0.5f;
			Plate003.localPosition = Pos003;
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (y)", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = -1f, maxValue = 1f),
			UI_FloatRange(minValue = -1f, maxValue = 1f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetY = 0f;

		private void onChanged_offsetY(object o)
		{
			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;

			Vector3 Pos = Pins.localPosition;
			Pos.y = offsetY;
			Pins.localPosition = Pos;

			MoveNode();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (x)", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = -2f, maxValue = 2f),
			UI_FloatRange(minValue = -2f, maxValue = 2f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetX = 0f;

		private void onChanged_offsetX(object o)
		{
			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;

			Vector3 Pos = Pins.localPosition;

			if(Is45(Pins))
			{
				Pos.x = 0.7071f * offsetX;
				Pos.z = -0.7071f * offsetX;
			}
			else
				Pos.x = offsetX;

			Pins.localPosition = Pos;

			MoveNode();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (z)", guiFormat = "F2",
			axisMode = KSPAxisMode.Incremental, minValue = -2f, maxValue = 2f),
			UI_FloatRange(minValue = -2f, maxValue = 2f, stepIncrement = 0.01f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetZ = 0f;

		private void onChanged_offsetZ(object o)
		{
			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;

			Vector3 Pos = Pins.localPosition;

			if(Is45(Pins))
			{
				Pos.x = -0.7071f * offsetZ;
				Pos.z = 0.7071f * offsetZ;
			}
			else
				Pos.z = offsetZ;

			Pins.localPosition = Pos;

			MoveNode();
		}

		////////////////////////////////////////
		// Context Menu

// FEHLER, später total ausblenden, das brauch ich nur für Debugging im Moment
		[KSPField(guiName = "Trunnion Port status", isPersistant = false, guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20)]
		public string DockStatus = "Ready";

		public void Enable()
		{
			fsm.RunEvent(on_enable);
		}

		public void Disable()
		{
			fsm.RunEvent(on_disable);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Disable Trunnion Pins")]
		public void TogglePort()
		{
			if(fsm.CurrentState == st_disabled)
				fsm.RunEvent(on_enable);
			else
				fsm.RunEvent(on_disable);
		}
/*
		void DeactivateColliders(Vessel v)
		{
			Collider[] colliders = part.transform.GetComponentsInChildren<Collider>(true);
			CollisionManager.SetCollidersOnVessel(v, true, colliders);
		}
/*
		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#autoLOC_236028")]
		public void EnableXFeed()
		{
			Events["EnableXFeed"].active = false;
			Events["DisableXFeed"].active = true;
			bool fuelCrossFeed = part.fuelCrossFeed;
			part.fuelCrossFeed = (crossfeed = true);
			if(fuelCrossFeed != crossfeed)
				GameEvents.onPartCrossfeedStateChange.Fire(base.part);
		}

		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "#autoLOC_236030")]
		public void DisableXFeed()
		{
			Events["EnableXFeed"].active = true;
			Events["DisableXFeed"].active = false;
			bool fuelCrossFeed = base.part.fuelCrossFeed;
			base.part.fuelCrossFeed = (crossfeed = false);
			if(fuelCrossFeed != crossfeed)
				GameEvents.onPartCrossfeedStateChange.Fire(base.part);
		}
*/
		////////////////////////////////////////
		// Actions

		[KSPAction("Enable")]
		public void EnableAction(KSPActionParam param)
		{ Enable(); }

		[KSPAction("Disable")]
		public void DisableAction(KSPActionParam param)
		{ Disable(); }
/*
		[KSPAction("#autoLOC_236028")]
		public void EnableXFeedAction(KSPActionParam param)
		{ EnableXFeed(); }

		[KSPAction("#autoLOC_236030")]
		public void DisableXFeedAction(KSPActionParam param)
		{ DisableXFeed(); }

		[KSPAction("#autoLOC_236032")]
		public void ToggleXFeedAction(KSPActionParam param)
		{
			if(crossfeed)
				DisableXFeed();
			else
				EnableXFeed();
		}
*/
		[KSPAction("#autoLOC_6001447")]
		public void MakeReferenceToggle(KSPActionParam act)
		{
			MakeReferenceTransform();
		}

		////////////////////////////////////////
		// Reference / Target

		[KSPEvent(guiActive = true, guiName = "#autoLOC_6001447")]
		public void MakeReferenceTransform()
		{
			part.SetReferenceTransform(controlTransform);
			vessel.SetReferenceTransform(part);
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = false, unfocusedRange = 200f, guiName = "#autoLOC_6001448")]
		public void SetAsTarget()
		{
			FlightGlobals.fetch.SetVesselTarget(this);
		}

		[KSPEvent(guiActive = false, guiActiveUnfocused = true, externalToEVAOnly = false, unfocusedRange = 200f, guiName = "#autoLOC_6001449")]
		public void UnsetTarget()
		{
			FlightGlobals.fetch.SetVesselTarget(null);
		}

		////////////////////////////////////////
		// IDockable

		private DockInfo dockInfo;

		public Part GetPart()
		{ return part; }

//GameObject fake; Transform fake_nodeTransform;
		public Transform GetNodeTransform()
		{ return nodeTransform; }
//		{ return fake_nodeTransform; }

		public Vector3 GetDockingOrientation()
		{ return dockingOrientation; }

		public int GetSnapCount()
		{ return snapCount; }

		public DockInfo GetDockInfo()
		{ return dockInfo; }

		public void SetDockInfo(DockInfo _dockInfo)
		{
			dockInfo = _dockInfo;
			vesselInfo =
				(dockInfo == null) ? null :
				((dockInfo.part == (IDockable)this) ? dockInfo.vesselInfo : dockInfo.targetVesselInfo);
		}

		public bool IsDocked()
		{
			return ((fsm.CurrentState == st_docked) || (fsm.CurrentState == st_preattached));
		}

		public IDockable GetOtherDockable()
		{
			return IsDocked() ? (IDockable)otherPort : null;
		}

		////////////////////////////////////////
		// ITargetable

		public Transform GetTransform()
		{
			return nodeTransform;
		}

		public Vector3 GetObtVelocity()
		{
			return base.vessel.obt_velocity;
		}

		public Vector3 GetSrfVelocity()
		{
			return base.vessel.srf_velocity;
		}

		public Vector3 GetFwdVector()
		{
			return nodeTransform.forward;
		}

		public Vessel GetVessel()
		{
			return vessel;
		}

		public string GetName()
		{
			return "name fehlt noch"; // FEHLER, einbauen
		}

		public string GetDisplayName()
		{
			return GetName();
		}

		public Orbit GetOrbit()
		{
			return vessel.orbit;
		}

		public OrbitDriver GetOrbitDriver()
		{
			return vessel.orbitDriver;
		}

		public VesselTargetModes GetTargetingMode()
		{
			return VesselTargetModes.DirectionVelocityAndOrientation;
		}

		public bool GetActiveTargetable()
		{
			return false;
		}

		////////////////////////////////////////
		// IModuleInfo

		string IModuleInfo.GetModuleTitle()
		{
			return "Trunnion Pins";
		}

		string IModuleInfo.GetInfo()
		{
			return "";
		}

		Callback<Rect> IModuleInfo.GetDrawModulePanelCallback()
		{
			return null;
		}

		string IModuleInfo.GetPrimaryField()
		{
			return null;
		}
	}
}
