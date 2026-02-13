using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

#if DEBUG
using PayloadRetentionSystemNext.Utility;
#endif
using DockingFunctions;


namespace PayloadRetentionSystemNext.Module
{
	public class ModuleTrunnionPins : PartModule, IDockable, ITargetable, IModuleInfo
	{
		// Settings

		[KSPField(isPersistant = false)]
		public string nodeType = "TrunnionPin";

		[KSPField(isPersistant = false), SerializeField]
		private string nodeTypesAccepted = "Trunnion";

		public HashSet<string> nodeTypesAcceptedS = null;


		[KSPField(isPersistant = false), SerializeField]
		public string nodeTransformName = "TrunnionPinsNode";

		[KSPField(isPersistant = false), SerializeField]
		public Vector3 dockingOrientation = Vector3.right; // defines the direction of the docking port (when docked at a 0° angle, these local vectors of two ports point into the same direction)

		[KSPField(isPersistant = false), SerializeField]
		public int snapCount = 2;


		[KSPField(guiFormat = "S", guiActive = true, guiActiveEditor = true, guiName = "Port Name")]
		public string portName = "";

		// Construction

		public ModuleTrunnionPins companion = null;
		public uint companionPartUId;

		public Vector3 companionOffset = Vector3.zero;

		// Docking and Status

		public BaseEvent evtSetAsTarget;
		public BaseEvent evtUnsetTarget;

		public Transform nodeTransform;

		public KerbalFSM fsm;

		public KFSMState st_inoperable;

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

			if(node.HasValue("portName"))
				portName = node.GetValue("portName");

			if((part.partInfo == null) || (part.partInfo.partPrefab == null)) // I assume, that I'm the prefab then
			{
				SetVisibility(portMode >= 2);

				UpdateDimension();
				UpdatePosition();
				UpdateRotation();
				UpdateNode();
			}

			if(node.HasValue("companionUId"))
				companionPartUId = uint.Parse(node.GetValue("companionUId"));

			if(node.HasValue("companionOffset"))
				companionOffset = ConfigNode.ParseVector3(node.GetValue("companionOffset"));

			if(node.HasValue("state"))
				DockStatus = node.GetValue("state");
			else
				DockStatus = "Ready";

			if(node.HasNode("DOCKEDVESSEL"))
			{
				vesselInfo = new DockedVesselInfo();
				vesselInfo.Load(node.GetNode("DOCKEDVESSEL"));
			}
		}

		public override void OnSave(ConfigNode node)
		{
			base.OnSave(node);

			node.AddValue("portName", portName);

			if(companion)
			{
				node.AddValue("companionUId", companionPartUId);
				node.AddValue("companionOffset", companionOffset);
			}

			node.AddValue("state", (string)(((fsm != null) && (fsm.Started)) ? fsm.currentStateName : DockStatus));

			if(vesselInfo != null)
				vesselInfo.Save(node.AddNode("DOCKEDVESSEL"));
		}

		public override void OnStart(StartState state)
		{
			base.OnStart(state);

			nodeTypesAcceptedS = new HashSet<string>();

			string[] values = nodeTypesAccepted.Split(new char[2] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries);
			foreach(string s in values)
				nodeTypesAcceptedS.Add(s);

			if(portName == string.Empty)
				portName = part.partInfo.title;

			if(state == StartState.Editor)
			{
				Fields["length"].OnValueModified += onChanged_length;
				Fields["width"].OnValueModified += onChanged_width;
				Fields["offsetY"].OnValueModified += onChanged_offset;
				Fields["offsetX"].OnValueModified += onChanged_offset;
				Fields["offsetZ"].OnValueModified += onChanged_offset;
				Fields["rotationX"].OnValueModified += onChanged_rotation;
				Fields["rotationY"].OnValueModified += onChanged_rotation;
				Fields["rotationZ"].OnValueModified += onChanged_rotation;
			}
			else
			{
				evtSetAsTarget = Events["SetAsTarget"];
				evtUnsetTarget = Events["UnsetTarget"];

				nodeTransform = part.FindModelTransform(nodeTransformName);
				if(!nodeTransform)
				{
					Logger.Log("No node transform found with name " + nodeTransformName, Logger.Level.Error);
					return;
				}
			}

			StartCoroutine(WaitAndInitialize(state));

	//		StartCoroutine(WaitAndDisableDockingNode());
		}

		public IEnumerator WaitAndInitialize(StartState st)
		{
			yield return null;

			Events["TogglePort"].active = false;

			if(companionPartUId != 0)
			{
				Part companionPart;

				while(!(companionPart = FlightGlobals.FindPartByID(companionPartUId)))
					yield return null;

				companion = companionPart.GetComponent<ModuleTrunnionPins>();
			}

			SetVisibility(portMode >= 2);

			UpdateDimension();
			UpdatePosition();
			UpdateRotation();
			UpdateNode();

			SetupFSM();

			fsm.StartFSM((portMode == 0) ? "Inoperable" : DockStatus);

			// fix -> the node needs to be re-initialized in the editor after some frames
			if(st == StartState.Editor)
			{
				yield return new WaitForFixedUpdate();
				yield return new WaitForFixedUpdate();
				yield return new WaitForFixedUpdate();

				UpdateNode();
			}
		}
	/*
		public IEnumerator WaitAndDisableDockingNode()
		{
			ModuleDockingNode DockingNode = part.FindModuleImplementing<ModuleDockingNode>();

			if(DockingNode)
			{
				while((DockingNode.fsm == null) || (!DockingNode.fsm.Started))
					yield return null;

				DockingNode.fsm.RunEvent(DockingNode.on_disable);
			}
		}
	*/
		public void OnDestroy()
		{
			Fields["length"].OnValueModified -= onChanged_length;
			Fields["width"].OnValueModified -= onChanged_width;
			Fields["offsetY"].OnValueModified -= onChanged_offset;
			Fields["offsetX"].OnValueModified -= onChanged_offset;
			Fields["offsetZ"].OnValueModified -= onChanged_offset;
			Fields["rotationX"].OnValueModified -= onChanged_rotation;
			Fields["rotationY"].OnValueModified -= onChanged_rotation;
			Fields["rotationZ"].OnValueModified -= onChanged_rotation;
		}

		////////////////////////////////////////
		// Functions

		public void SetupFSM()
		{
			fsm = new KerbalFSM();

			st_inoperable = new KFSMState("Inoperable");
			st_inoperable.OnEnter = delegate(KFSMState from)
			{ SetVisibility(false); };
			fsm.AddState(st_inoperable);

			st_passive = new KFSMState("Ready");
			st_passive.OnEnter = delegate(KFSMState from)
			{
				otherPort = null;
				dockedPartUId = 0;

				Events["TogglePort"].guiName = "Deactivate Trunnion Pins";
				Events["TogglePort"].active = true;

				DockStatus = st_passive.name;		
			};
			st_passive.OnFixedUpdate = delegate
			{
			};
			st_passive.OnLeave = delegate(KFSMState to)
			{
				if(to != st_disabled)
					Events["TogglePort"].active = false;
			};
			fsm.AddState(st_passive);

			st_approaching_passive = new KFSMState("Approaching");
			st_approaching_passive.OnEnter = delegate(KFSMState from)
			{
				DockStatus = st_approaching_passive.name;		
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
				DockStatus = st_latched_passive.name;		
			};
			st_latched_passive.OnFixedUpdate = delegate
			{
			};
			st_latched_passive.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_latched_passive);

			st_docked = new KFSMState("Docked");
			st_docked.OnEnter = delegate(KFSMState from)
			{
				DockStatus = st_docked.name;		
			};
			st_docked.OnFixedUpdate = delegate
			{
			};
			st_docked.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_docked);

			st_preattached = new KFSMState("Attached");
			st_preattached.OnEnter = delegate(KFSMState from)
			{
				DockStatus = st_preattached.name;		
			};
			st_preattached.OnFixedUpdate = delegate
			{
			};
			st_preattached.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_preattached);

			st_disabled = new KFSMState("Inactive");
			st_disabled.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].guiName = "Activate Trunnion Pins";
				Events["TogglePort"].active = true;

				DockStatus = st_disabled.name;		
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

		internal void SetVisibility(bool visible)
		{
			UpdateDimension();

			Transform Plate001 = KSPUtil.FindInPartModel(transform, "Plate.001");
			Transform Plate003 = KSPUtil.FindInPartModel(transform, "Plate.003");

			foreach(MeshRenderer r in Plate001.GetComponentsInChildren<MeshRenderer>())
				r.enabled = visible;

			foreach(Collider c in Plate001.GetComponentsInChildren<Collider>())
				c.enabled = visible;

			foreach(MeshRenderer r in Plate003.GetComponentsInChildren<MeshRenderer>())
				r.enabled = visible;
			
			foreach(Collider c in Plate003.GetComponentsInChildren<Collider>())
				c.enabled = visible;

			// attach-Node entfernen - FEHLER, fehlt?

			AttachNode n = part.FindAttachNode("TrunnionPinsNode");

			if(n != null)
			{
				if(visible)
				{
					n.nodeType = AttachNode.NodeType.Stack;
					n.radius = 0.4f;
				}
				else
				{
					n.nodeType = AttachNode.NodeType.Dock;
					n.radius = 0.001f;
				}
			}
		}

		internal void UpdateDimension()
		{
			Transform Plate000 = KSPUtil.FindInPartModel(transform, "Plate.000");
			Transform Plate001 = KSPUtil.FindInPartModel(transform, "Plate.001");
			Transform Plate002 = KSPUtil.FindInPartModel(transform, "Plate.002");
			Transform Plate003 = KSPUtil.FindInPartModel(transform, "Plate.003");

			if(Plate000)
			{
				Vector3 Pos000 = Plate000.localPosition;
				Pos000.x = (portMode > 0) ? length * 0.5f : 0f;
				Pos000.z = -1.231f - width * 0.5f;
				Plate000.localPosition = Pos000;
			}

			if(Plate001)
			{
				Vector3 Pos001 = Plate001.localPosition;
				Pos001.x = -length * 0.5f;
				Pos001.z = -1.231f - width * 0.5f;
				Plate001.localPosition = Pos001;
			}

			if(Plate002)
			{
				Vector3 Pos002 = Plate002.localPosition;
				Pos002.x = (portMode > 0) ? length * 0.5f : 0f;
				Pos002.z = 1.231f + width * 0.5f;
				Plate002.localPosition = Pos002;
			}

			if(Plate003)
			{
				Vector3 Pos003 = Plate003.localPosition;
				Pos003.x = -length * 0.5f;
				Pos003.z = 1.231f + width * 0.5f;
				Plate003.localPosition = Pos003;
			}
		}

		internal void UpdatePosition()
		{
			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;
			Pins.localPosition = new Vector3(offsetX, offsetY, offsetZ) + companionOffset;
			UpdateNode();
		}

		internal void UpdateRotation()
		{
			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;
			Pins.localRotation = Quaternion.AngleAxis(rotationX, Vector3.right) * Quaternion.AngleAxis(rotationY, Vector3.up) * Quaternion.AngleAxis(rotationZ, Vector3.forward);
			UpdateNode();
		}

		internal void UpdateNode()
		{
			part.UpdateAttachNodes();
		}

// FEHLER, hier das Zeug echt genauer setzen und so... und vor allem die Position prüfen, ob das überhaupt geht, sonst verwerfen
		internal void SetCompanion(ModuleTrunnionPins other)
		{
			if(portMode == 2)
				return;

			companion = other;
			other.companion = this;

			// FEHLER, wir gehen im Moment einfach vom optimalen Fall aus -> der andere muss 2 und passiv sein

			companion.portMode = 0; // 2 passiv
			portMode = 1; // 2 aktiv

			Transform Pins = KSPUtil.FindInPartModel(transform, "TrunnionPinsNode").parent;
			Transform otherPins = KSPUtil.FindInPartModel(companion.transform, "TrunnionPinsNode").parent;

			// if .x und .z sind nicht 0, oder genug klein, dann abbrechen, weil's nicht passt? wäre das eine Idee?

			companionOffset = Pins.parent.InverseTransformVector(otherPins.position - Pins.position) * 0.5f;

			length = -2f * (Quaternion.Inverse(Pins.localRotation) * companionOffset).x;

			UpdateDimension();
			UpdatePosition();
			UpdateNode();
		}

		internal void ClearCompanion()
		{
			if(portMode == 2)
				return;

			ModuleTrunnionPins _c = companion;
			companion = null;

			if(_c)
				_c.ClearCompanion();

			length = 0; // FEHLER, klären, ob das immer 0 sein soll, wenn wir mode 0 oder 1 haben

			UpdatePosition();
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
						fsm.UpdateFSM();

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

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Distance (x)", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = 0.6f, maxValue = 8f),
			UI_FloatRange(minValue = 0.0f, maxValue = 8f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float length = 2f;

		private void onChanged_length(object o)
		{
			UpdateDimension();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Distance (z)", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = -0.6f, maxValue = 0.6f),
			UI_FloatRange(minValue = -0.6f, maxValue = 0.6f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float width = 0f;

		private void onChanged_width(object o)
		{
			UpdateDimension();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (x)", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = -2f, maxValue = 2f),
			UI_FloatRange(minValue = -2f, maxValue = 2f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetX = 0f;

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (y)", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = -1f, maxValue = 1f),
			UI_FloatRange(minValue = -5f, maxValue = 5f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetY = 0f;

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Position (z)", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = -2f, maxValue = 2f),
			UI_FloatRange(minValue = -2f, maxValue = 2f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float offsetZ = 0f;

		private void onChanged_offset(object o)
		{
			UpdatePosition();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Rotation (x)", guiFormat = "F1",
			axisMode = KSPAxisMode.Incremental, minValue = -180f, maxValue = 180f),
			UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 0.1f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float rotationX = 0f;

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Rotation (y)", guiFormat = "F1",
			axisMode = KSPAxisMode.Incremental, minValue = -180f, maxValue = 180f),
			UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 0.1f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float rotationY = 0f;

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Trunnion-Pin Rotation (z)", guiFormat = "F1",
			axisMode = KSPAxisMode.Incremental, minValue = -180f, maxValue = 180f),
			UI_FloatRange(minValue = -180f, maxValue = 180f, stepIncrement = 0.1f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float rotationZ = 0f;

		private void onChanged_rotation(object o)
		{
			UpdateRotation();
		}

		////////////////////////////////////////
		// Context Menu

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

		[KSPField(isPersistant = true)]
		public int portMode = 2;

		private void onChanged_portMode(object o)
		{
			switch(portMode)
			{
			case 0:
				Events["TogglePortMode"].guiName = "Port Mode: 2 Pins - passive"; SetVisibility(false); break;
			case 1:
				Events["TogglePortMode"].guiName = "Port Mode: 2 Pins - active"; SetVisibility(false); break;
			case 2:
				Events["TogglePortMode"].guiName = "Port Mode: 4 Pins"; SetVisibility(true); break;
			}
		}

		public int PortMode
		{
			get { return portMode; }
			set { if(portMode == value) return; portMode = value; onChanged_portMode(null); }
		}

		[KSPEvent(guiActive = true, guiActiveEditor = true, guiName = "Port Mode: 4 Pins")]
		public void TogglePortMode()
		{
			portMode = (portMode + 1) % 3;
			onChanged_portMode(null);
		}

		////////////////////////////////////////
		// Actions

		[KSPAction("Enable")]
		public void EnableAction(KSPActionParam param)
		{ Enable(); }

		[KSPAction("Disable")]
		public void DisableAction(KSPActionParam param)
		{ Disable(); }

		////////////////////////////////////////
		// Reference / Target

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

		public Transform GetNodeTransform()
		{ return nodeTransform; }

		public Vector3 GetDockingOrientation()
		{ return dockingOrientation; }

		public int GetSnapCount()
		{ return snapCount; }

		public DockInfo GetDockInfo()
		{ return dockInfo; }

		public void SetDockInfo(DockInfo _dockInfo)
		{
			dockInfo = _dockInfo;

			if(dockInfo == null)
				vesselInfo = null;
			else if(dockInfo.part == (IDockable)this)
				vesselInfo = dockInfo.vesselInfo;
			else
				vesselInfo = dockInfo.targetVesselInfo;
		}

		// returns true, if the port is compatible with the other port
		public bool IsCompatible(IDockable otherPort)
		{
			if(otherPort == null)
				return false;

			ModuleTrunnionLatches _otherPort = otherPort.GetPart().GetComponent<ModuleTrunnionLatches>();

			if(!_otherPort)
				return false;

			if(!nodeTypesAcceptedS.Contains(_otherPort.nodeType)
			|| !_otherPort.nodeTypesAcceptedS.Contains(nodeType))
				return false;

			return true;
		}

		// returns true, if the port is (passive and) ready to dock with an other (active) port
		public bool IsReadyFor(IDockable otherPort)
		{
			if(otherPort != null)
			{
				if(!IsCompatible(otherPort))
					return false;
			}

			return (fsm.CurrentState == st_passive);
		}

		public ITargetable GetTargetable()
		{
			return (ITargetable)this;
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
			return vessel.obt_velocity;
		}

		public Vector3 GetSrfVelocity()
		{
			return vessel.srf_velocity;
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
			return portName;
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

		private DockingPortRenameDialog renameDialog;

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Rename Port")]
		public void Rename()
		{
			InputLockManager.SetControlLock("dockingPortRenameDialog");

			renameDialog = DockingPortRenameDialog.Spawn(portName, onPortRenameAccept, onPortRenameCancel);
		}

		private void onPortRenameAccept(string newPortName)
		{
			portName = newPortName;
			onPortRenameCancel();
		}

		private void onPortRenameCancel()
		{
			InputLockManager.RemoveControlLock("dockingPortRenameDialog");
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
