using System;
using System.Collections;
using System.Collections.Generic;

using UnityEngine;

using DockingFunctions;


namespace PayloadRetentionSystemNext.Module
{
	public class ModuleTrunnionLatches : PartModule, IDockable, ITargetable, IModuleInfo
	{
		// Settings

		[KSPField(isPersistant = false), SerializeField]
		public string nodeTransformName = "TrunnionPortNode";

		[KSPField(isPersistant = false), SerializeField]
		public string referenceAttachNode = "TrunnionPortNode"; // if something is connected to this node, then the state is "Attached" (or "Pre-Attached" -> connected in the VAB/SPH)

		[KSPField(isPersistant = false), SerializeField]
		public Vector3 dockingOrientation = Vector3.right; // defines the direction of the docking port (when docked at a 0° angle, these local vectors of two ports point into the same direction)

		[KSPField(isPersistant = false), SerializeField]
		public int snapCount = 2;


		[KSPField(isPersistant = false), SerializeField]
		public float detectionDistance = 5f;

		[KSPField(isPersistant = false), SerializeField]
		public float approachingDistance = 0.3f;

		[KSPField(isPersistant = false), SerializeField]
		public float captureDistance = 0.03f;


		[KSPField(isPersistant = false)]
		public string nodeType = "Trunnion";

		[KSPField(isPersistant = false), SerializeField]
		private string nodeTypesAccepted = "TrunnionPin";

		public HashSet<string> nodeTypesAcceptedS = null;


		[KSPField(isPersistant = false), SerializeField]
		public float latchingSpeedRotation = 0.1f; // degrees per second

		[KSPField(isPersistant = false), SerializeField]
		public float latchingSpeedTranslation = 0.025f; // distance per second


		[KSPField(guiFormat = "S", guiActive = true, guiActiveEditor = true, guiName = "Port Name")]
		public string portName = "";

		// Docking and Status

		public BaseEvent evtSetAsTarget;
		public BaseEvent evtUnsetTarget;

		public Transform nodeTransform;

		public KerbalFSM fsm;

		public KFSMState st_active;			// "active" / "searching"

		public KFSMState st_approaching;	// port found

		public KFSMState st_latching;		// orienting and retracting in progress
		public KFSMState st_prelatched;		// ready to dock
		public KFSMState st_latched;		// docked

		public KFSMState st_unlatching;		// opening latches

		public KFSMState st_docked;			// docked or docked_to_same_vessel
		public KFSMState st_preattached;

		public KFSMState st_disabled;


		public KFSMEvent on_approach;
		public KFSMEvent on_distance;

		public KFSMEvent on_latching;
		public KFSMEvent on_prelatch;
		public KFSMEvent on_latch;

		public KFSMEvent on_unlatching;

		public KFSMEvent on_release;

		public KFSMEvent on_dock;
		public KFSMEvent on_undock;

		public KFSMEvent on_enable;
		public KFSMEvent on_disable;

		public KFSMEvent on_construction;

		// Sounds

/* FEHLER, Sound fehlt noch total
 * 
		[KSPField(isPersistant = false)] public string preAttachSoundFilePath = "";
		[KSPField(isPersistant = false)] public string latchSoundFilePath = "";
		[KSPField(isPersistant = false)] public string detachSoundFilePath = "";
		
		[KSPField(isPersistant = false)] public string activatingSoundFilePath = "";
		[KSPField(isPersistant = false)] public string activatedSoundFilePath = "";
		[KSPField(isPersistant = false)] public string deactivatingSoundFilePath = "";

		protected SoundSource soundSound = null;
*/

		// Capturing / Docking

		public ModuleTrunnionPins otherPort;
		public uint dockedPartUId;

		public DockedVesselInfo vesselInfo;

		private bool inCaptureDistance = false;

		private ConfigurableJoint latchJoint;

		private float latchJointBreakForce;
		private float latchJointBreakTorque;

		private Quaternion latchJointTargetRotation;
		private Vector3 latchJointTargetPosition;

		private Vector3 latchJointInitialPosition;

		private float latchProgress;
		private float latchProgressStep = 0.0005f;

		private int latchRelaxCounter;

		// Packed / OnRails

		private int followOtherPort = 0;

		private Vector3 otherPortRelativePosition;
		private Quaternion otherPortRelativeRotation;

		////////////////////////////////////////
		// Constructor

		public ModuleTrunnionLatches()
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

		//	if(node.HasValue("length"))
		//		length = float.Parse(node.GetValue("length"));

			if(node.HasValue("state"))
				DockStatus = node.GetValue("state");
			else
				DockStatus = "Inactive";

			if(node.HasValue("dockUId"))
				dockedPartUId = uint.Parse(node.GetValue("dockUId"));

			if(node.HasNode("DOCKEDVESSEL"))
			{
				vesselInfo = new DockedVesselInfo();
				vesselInfo.Load(node.GetNode("DOCKEDVESSEL"));
			}

			if(node.HasValue("followOtherPort"))
			{
				followOtherPort = int.Parse(node.GetValue("followOtherPort"));

				node.TryGetValue("otherPortRelativePosition", ref otherPortRelativePosition);
				node.TryGetValue("otherPortRelativeRotation", ref otherPortRelativeRotation);
			}
		}

		public override void OnSave(ConfigNode node)
		{
			base.OnSave(node);

		//	node.AddValue("length", length);

			node.AddValue("state", (string)(((fsm != null) && (fsm.Started)) ? fsm.currentStateName : DockStatus));

			node.AddValue("dockUId", dockedPartUId);

			if(vesselInfo != null)
				vesselInfo.Save(node.AddNode("DOCKEDVESSEL"));

			node.AddValue("followOtherPort", followOtherPort);

			if(followOtherPort != 0)
			{
				if(otherPortRelativePosition != null)	node.AddValue("otherPortRelativePosition", otherPortRelativePosition);
				if(otherPortRelativeRotation != null)	node.AddValue("otherPortRelativeRotation", otherPortRelativeRotation);
			}
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
			}

			UpdateDimension();

			if(state == StartState.Editor)
				return;

			GameEvents.onVesselGoOnRails.Add(OnPack);
			GameEvents.onVesselGoOffRails.Add(OnUnpack);

			GameEvents.OnEVAConstructionModePartDetached.Add(OnEVAConstructionModePartDetached);

			nodeTransform = base.part.FindModelTransform(nodeTransformName);
			if(!nodeTransform)
			{
				Debug.LogWarning("[Docking Node Module]: WARNING - No node transform found with name " + nodeTransformName, base.part.gameObject);
				return;
			}

			StartCoroutine(WaitAndInitialize(state));

	//		StartCoroutine(WaitAndDisableDockingNode());
		}

		public IEnumerator WaitAndInitialize(StartState st)
		{
			yield return null;

			Events["TogglePort"].active = false;

			Events["Latch"].active = false;
			Events["Release"].active = false;

			Events["Dock"].active = false;
			Events["Undock"].active = false;

			if(dockedPartUId != 0)
			{
				Part otherPart;

				while(!(otherPart = FlightGlobals.FindPartByID(dockedPartUId)))
					yield return null;

				otherPort = otherPart.GetComponent<ModuleTrunnionPins>();

				otherPort.otherPort = this;
				otherPort.dockedPartUId = part.flightID;
			}

			if((DockStatus == "Inactive")
			|| ((DockStatus == "Attached") && (otherPort == null)))
			{
				if(referenceAttachNode != string.Empty)
				{
					AttachNode node = part.FindAttachNode(referenceAttachNode);
					if((node != null) && node.attachedPart)
					{
						ModuleTrunnionPins _otherPort = node.attachedPart.GetComponent<ModuleTrunnionPins>();

						if(_otherPort)
						{
							otherPort = _otherPort;
							dockedPartUId = otherPort.part.flightID;

							DockStatus = "Attached";
							otherPort.DockStatus = "Attached";
						}
					}
				}
			}

			SetupFSM();

			if((DockStatus == "Approaching")
			|| (DockStatus == "Latching")
			|| (DockStatus == "Pre Latched")
			|| (DockStatus == "Latched")
			|| (DockStatus == "Unlatching"))
			{
				if(otherPort != null)
				{
					while(!otherPort.part.started || (otherPort.fsm == null) || (!otherPort.fsm.Started))
						yield return null;
				}
			}

			if(DockStatus == "Pre Latched")
				DockStatus = "Latching";

			if((DockStatus == "Latched")
			|| (DockStatus == "Unlatching"))
			{
				BuildLatchJoint();
				CalculateLatchJointTarget();
			}

			if(DockStatus == "Docked")
			{
				otherPort.DockStatus = "Docked";

				DockingHelper.OnLoad(this, vesselInfo, otherPort, otherPort.vesselInfo);
			}

			fsm.StartFSM(DockStatus);
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

			GameEvents.onVesselGoOnRails.Remove(OnPack);
			GameEvents.onVesselGoOffRails.Remove(OnUnpack);

			GameEvents.OnEVAConstructionModePartDetached.Remove(OnEVAConstructionModePartDetached);
		}

		private void OnPack(Vessel v)
		{
			if(vessel == v)
			{
				if(DockStatus == "Latched")
				{
					if(Vessel.GetDominantVessel(vessel, otherPort.vessel) == otherPort.vessel)
					{
						followOtherPort = 1;
						VesselPositionManager.Register(part, otherPort.part, true, out otherPortRelativePosition, out otherPortRelativeRotation);
					}
					else
					{
						followOtherPort = 2;
						VesselPositionManager.Register(otherPort.part, part, true, out otherPortRelativePosition, out otherPortRelativeRotation);
					}
				}
			}
		}

		private void OnUnpack(Vessel v)
		{
			if(vessel == v)
			{
				if(DockStatus == "Latched")
				{
					VesselPositionManager.Unregister((followOtherPort == 1) ? vessel : otherPort.vessel);
					followOtherPort = 0;
				}
			}
		}

		private void OnEVAConstructionModePartDetached(Vessel v, Part p)
		{
			if(part == p)
			{
				if(otherPort)
				{
					otherPort.otherPort = null;
					otherPort.dockedPartUId = 0;
				//	otherPort.fsm.RunEvent(otherPort.on_construction);
				}

				otherPort = null;
				dockedPartUId = 0;
				fsm.RunEvent(on_construction);
			}
		}

		////////////////////////////////////////
		// Functions

static float baseForce = 1000f;

		public void SetupFSM()
		{
			fsm = new KerbalFSM();

			st_active = new KFSMState("Ready");
			st_active.OnEnter = delegate(KFSMState from)
			{
				otherPort = null;
				dockedPartUId = 0;

				Events["TogglePort"].guiName = "Deactivate End Effector";
				Events["TogglePort"].active = true;

				DockStatus = st_active.name;
			};
			st_active.OnFixedUpdate = delegate
			{
				Vector3 distance; float angle;

				for(int i = 0; i < FlightGlobals.VesselsLoaded.Count; i++)
				{
					Vessel vessel = FlightGlobals.VesselsLoaded[i];

					if(vessel.packed
						/*|| (vessel == part.vessel)*/) // no docking to ourself is possible
						continue;

					for(int j = 0; j < vessel.dockingPorts.Count; j++)
					{
						PartModule partModule = vessel.dockingPorts[j];

						if((partModule.part == null)
						/*|| (partModule.part == part)*/ // no docking to ourself is possible
						|| (partModule.part.State == PartStates.DEAD))
							continue;

						ModuleTrunnionPins _otherPort = partModule.GetComponent<ModuleTrunnionPins>();

						if(_otherPort == null)
							continue;

						if(_otherPort.fsm.CurrentState != _otherPort.st_passive)
							continue;

						distance = _otherPort.nodeTransform.position - nodeTransform.position;

						if(distance.magnitude < detectionDistance)
						{
							angle = Vector3.Angle(nodeTransform.forward, -_otherPort.nodeTransform.forward);

							DockDistance = distance.magnitude.ToString();
							DockAngle = "-";

							if((angle <= 15f) && (distance.magnitude <= approachingDistance))
							{
								otherPort = _otherPort;
								dockedPartUId = otherPort.part.flightID;

								fsm.RunEvent(on_approach);
								otherPort.fsm.RunEvent(otherPort.on_approach_passive);
								return;
							}
						}
					}
				}

				DockDistance = "-";
				DockAngle = "-";
			};
			st_active.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_active);

			st_approaching = new KFSMState("Approaching");
			st_approaching.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].active = false;

				inCaptureDistance = false;

				otherPort.otherPort = this;
				otherPort.dockedPartUId = part.flightID;

				DockStatus = st_approaching.name;
			};
			st_approaching.OnFixedUpdate = delegate
			{
				if(Mathf.Abs(length - otherPort.length) > 0.01)
				{
					DockDistance = "wrong length";
					DockAngle = "-";

					return;
				}

				Vector3 distance = otherPort.nodeTransform.position - nodeTransform.position;

				DockDistance = distance.magnitude.ToString();

				if(distance.magnitude < captureDistance)
				{
					Vector3 tvref = nodeTransform.TransformDirection(dockingOrientation);
					Vector3 tv = otherPort.nodeTransform.TransformDirection(otherPort.dockingOrientation);
//					Vector3 tvref = nodeTransform.right;
//					Vector3 tv = otherPort.nodeTransform.right;
					float ang = Vector3.SignedAngle(tvref, tv, -nodeTransform.forward);

					ang = 360f + ang - (180f / snapCount);
					ang %= (360f / snapCount);
					ang -= (180f / snapCount);

					bool angleok = ((ang > -5f) && (ang < 5f));

					DockAngle = ang.ToString();

					if(angleok)
					{
						if(!inCaptureDistance)
							Events["Latch"].active = true;

						inCaptureDistance = true;

						return;
					}
				}
				else
					DockAngle = "-";

				if(inCaptureDistance)
					Events["Latch"].active = false;

				inCaptureDistance = false;
				
				if(distance.magnitude < 1.5f * approachingDistance)
				{
					float angle = Vector3.Angle(nodeTransform.forward, -otherPort.nodeTransform.forward);

					if(angle <= 15f)
						return;
				}

				otherPort.fsm.RunEvent(otherPort.on_distance_passive);
				fsm.RunEvent(on_distance);
			};
			st_approaching.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_approaching);

			st_latching = new KFSMState("Latching");
			st_latching.OnEnter = delegate(KFSMState from)
			{
				Events["Latch"].active = false;
				Events["Release"].active = true;

				BuildLatchJoint();
				CalculateLatchJointTarget();

				float latchingDuration = Math.Max(
						(nodeTransform.position - otherPort.nodeTransform.position).magnitude / latchingSpeedTranslation,
						(Quaternion.Angle(latchJointTargetRotation, Quaternion.identity) / latchingSpeedRotation));

				if(float.IsNaN(latchingDuration) || float.IsInfinity(latchingDuration))
					latchingDuration = 10;

				latchProgress = 1;
				latchProgressStep = TimeWarp.fixedDeltaTime / latchingDuration;

				latchJointInitialPosition = latchJoint.targetPosition;

				part.GetComponent<ModuleAnimateGeneric>().Toggle();

				DockStatus = st_latching.name;
			};
			st_latching.OnFixedUpdate = delegate
			{
				if((latchProgress = part.GetComponent<ModuleAnimateGeneric>().Progress) == 1f)
				{
// FEHLER, neu, das hier prüfen...
					float JointForce = float.IsPositiveInfinity(PhysicsGlobals.JointForce) ? 1000000f : Mathf.Min(1000000f, PhysicsGlobals.JointForce);

					float force1 = (1f - latchProgress) * JointForce + latchProgress * 100f;
					float force2 = (1f - latchProgress) * 60000f + latchProgress * 100f;

					JointDrive angularDrive = new JointDrive { maximumForce = force1, positionSpring = force2, positionDamper = 0f };
					latchJoint.angularXDrive = latchJoint.angularYZDrive = latchJoint.slerpDrive = angularDrive;

					JointDrive linearDrive = new JointDrive { maximumForce = force1, positionSpring = force1, positionDamper = 0f };
					latchJoint.xDrive = latchJoint.yDrive = latchJoint.zDrive = linearDrive;

					latchJoint.targetRotation = latchJointTargetRotation;
					latchJoint.targetPosition = latchJointTargetPosition;

					fsm.RunEvent(on_prelatch);
				}
				else
				{
					latchJoint.targetRotation = Quaternion.Slerp(latchJointTargetRotation, Quaternion.identity, latchProgress);
					latchJoint.targetPosition = Vector3.Lerp(latchJointTargetPosition, latchJointInitialPosition, latchProgress);
				}
			};
			st_latching.OnLeave = delegate(KFSMState to)
			{
				if(to != st_prelatched)
					Events["Release"].active = false;
			};
			fsm.AddState(st_latching);

			st_prelatched = new KFSMState("Pre Latched");
			st_prelatched.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = true;

				latchRelaxCounter = 10;

				DockStatus = st_prelatched.name;
			};
			st_prelatched.OnFixedUpdate = delegate
			{
				if(--latchRelaxCounter < 0)
				{
					fsm.RunEvent(on_latch);
					otherPort.fsm.RunEvent(otherPort.on_latch_passive);
				}
			};
			st_prelatched.OnLeave = delegate(KFSMState to)
			{
				if(to != st_latched)
					Events["Release"].active = false;
			};
			fsm.AddState(st_prelatched);
		
			st_latched = new KFSMState("Latched");
			st_latched.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = true;

				Events["Dock"].active = true;
				Events["Undock"].active = false;

				JointDrive angularDrive = new JointDrive { maximumForce = PhysicsGlobals.JointForce, positionSpring = 60000f, positionDamper = 0f };
				latchJoint.angularXDrive = latchJoint.angularYZDrive = latchJoint.slerpDrive = angularDrive;

				JointDrive linearDrive = new JointDrive { maximumForce = PhysicsGlobals.JointForce, positionSpring = PhysicsGlobals.JointForce, positionDamper = 0f };
				latchJoint.xDrive = latchJoint.yDrive = latchJoint.zDrive = linearDrive;

				DockStatus = st_latched.name;
			};
			st_latched.OnFixedUpdate = delegate
			{
			};
			st_latched.OnLeave = delegate(KFSMState to)
			{
				Events["Release"].active = false;

				Events["Dock"].active = false;
			};
			fsm.AddState(st_latched);


			st_unlatching = new KFSMState("Unlatching");
			st_unlatching.OnEnter = delegate(KFSMState from)
			{
				Events["Release"].active = false;
				Events["Latch"].active = false;
				Events["Dock"].active = false;

				part.GetComponent<ModuleAnimateGeneric>().Toggle();

				DockStatus = st_unlatching.name;
			};
			st_unlatching.OnFixedUpdate = delegate
			{
				if(part.GetComponent<ModuleAnimateGeneric>().Progress == 1f)
				{
					DestroyLatchJoint();

					if(otherPort != null)
						otherPort.fsm.RunEvent(otherPort.on_release_passive);

					fsm.RunEvent(on_release);
				}
				else
				{
// FEHLER, ich probier mal was...

			JointDrive drive =
				new JointDrive
				{
					positionSpring = (1f - part.GetComponent<ModuleAnimateGeneric>().Progress) * baseForce,
					positionDamper = 0f,
					maximumForce = PhysicsGlobals.JointForce
				};

					latchJoint.xDrive = latchJoint.yDrive = latchJoint.zDrive = drive;
				}
			};
			st_unlatching.OnLeave = delegate(KFSMState to)
			{
			};
			fsm.AddState(st_unlatching);


			st_docked = new KFSMState("Docked");
			st_docked.OnEnter = delegate(KFSMState from)
			{
				Events["Undock"].active = true;

				DockStatus = st_docked.name;
			};
			st_docked.OnFixedUpdate = delegate
			{
			};
			st_docked.OnLeave = delegate(KFSMState to)
			{
				Events["Undock"].active = false;
			};
			fsm.AddState(st_docked);

			st_preattached = new KFSMState("Attached");
			st_preattached.OnEnter = delegate(KFSMState from)
			{
				Events["Undock"].active = true;

				DockStatus = st_preattached.name;
			};
			st_preattached.OnFixedUpdate = delegate
			{
			};
			st_preattached.OnLeave = delegate(KFSMState to)
			{
				Events["Undock"].active = false;
			};
			fsm.AddState(st_preattached);

			st_disabled = new KFSMState("Inactive");
			st_disabled.OnEnter = delegate(KFSMState from)
			{
				Events["TogglePort"].guiName = "Activate End Effector";
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


			on_approach = new KFSMEvent("Approaching");
			on_approach.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_approach.GoToStateOnEvent = st_approaching;
			fsm.AddEvent(on_approach, st_active);

			on_distance = new KFSMEvent("Distancing");
			on_distance.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_distance.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_distance, st_approaching, st_docked, st_preattached);

			on_latching = new KFSMEvent("Latch");
			on_latching.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_latching.GoToStateOnEvent = st_latching;
			fsm.AddEvent(on_latching, st_approaching);

			on_prelatch = new KFSMEvent("Pre Latch");
			on_prelatch.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_prelatch.GoToStateOnEvent = st_prelatched;
			fsm.AddEvent(on_prelatch, st_latching);

			on_latch = new KFSMEvent("Latched");
			on_latch.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_latch.GoToStateOnEvent = st_latched;
			fsm.AddEvent(on_latch, st_prelatched);


			on_unlatching = new KFSMEvent("Unlatch");
			on_unlatching.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_unlatching.GoToStateOnEvent = st_unlatching;
			fsm.AddEvent(on_unlatching, st_latched);


			on_release = new KFSMEvent("Released");
			on_release.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_release.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_release, st_unlatching);


			on_dock = new KFSMEvent("Perform docking");
			on_dock.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_dock.GoToStateOnEvent = st_docked;
			fsm.AddEvent(on_dock, st_latched);

			on_undock = new KFSMEvent("Undock");
			on_undock.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_undock.GoToStateOnEvent = st_latched;
			fsm.AddEvent(on_undock, st_docked, st_preattached);


			on_enable = new KFSMEvent("Enable");
			on_enable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_enable.GoToStateOnEvent = st_active;
			fsm.AddEvent(on_enable, st_disabled);

			on_disable = new KFSMEvent("Disable");
			on_disable.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_disable.GoToStateOnEvent = st_disabled;
			fsm.AddEvent(on_disable, st_active);


			on_construction = new KFSMEvent("Construction");
			on_construction.updateMode = KFSMUpdateMode.MANUAL_TRIGGER;
			on_construction.GoToStateOnEvent = st_disabled;
			fsm.AddEvent(on_construction, st_active, st_approaching, st_latching, st_prelatched, st_latched, st_unlatching, st_docked, st_preattached);
		}

		private void BuildLatchJoint()
		{
			// Joint
			ConfigurableJoint joint = gameObject.AddComponent<ConfigurableJoint>();

			joint.connectedBody = otherPort.part.Rigidbody;
joint.enableCollision = true; // FEHLER, hier brauch ich das wohl ... -> klären, wo das noch sein müsste vielleicht??

			joint.breakForce = joint.breakTorque = Mathf.Infinity;
// FEHLER FEHLER -> breakForce min von beiden und torque auch

			// we calculate with the "stack" force -> thus * 4f and not * 1.6f

			float breakingForceModifier = 1f;
			float breakingTorqueModifier = 1f;

			latchJointBreakForce = Mathf.Min(part.breakingForce, otherPort.part.breakingForce) *
				breakingForceModifier * 4f;

			latchJointBreakTorque = Mathf.Min(part.breakingTorque, otherPort.part.breakingTorque) *
				breakingTorqueModifier * 4f;

			joint.breakForce = latchJointBreakForce;
			joint.breakTorque = latchJointBreakTorque;


			joint.xMotion = joint.yMotion = joint.zMotion = ConfigurableJointMotion.Free;
			joint.angularXMotion = joint.angularYMotion = joint.angularZMotion = ConfigurableJointMotion.Free;

			JointDrive drive =
				new JointDrive
				{
					positionSpring = 100f,
					positionDamper = 0f,
					maximumForce = 100f
				};

			joint.angularXDrive = joint.angularYZDrive = joint.slerpDrive = drive;
			joint.xDrive = joint.yDrive = joint.zDrive = drive;

			latchJoint = joint;

			DockDistance = "-";
			DockAngle = "-";
		}

		private void CalculateLatchJointTarget()
		{
			Vector3 targetPosition; Quaternion targetRotation;
			DockingHelper.CalculateDockingPositionAndRotation(this, otherPort, out targetPosition, out targetRotation);

			// convert values from org-values to real values (for latching we need real values, for docking org-values)
			targetPosition +=
				otherPort.GetPart().transform.position
				- (otherPort.GetPart().vessel.transform.position + otherPort.GetPart().vessel.transform.rotation * otherPort.GetPart().orgPos);

			targetRotation *=
				Quaternion.Inverse(otherPort.GetPart().vessel.transform.rotation * otherPort.GetPart().orgRot)
				* otherPort.GetPart().transform.rotation;

			// invert both values
			latchJointTargetPosition = -transform.InverseTransformPoint(targetPosition);
			latchJointTargetRotation = Quaternion.Inverse(Quaternion.Inverse(transform.rotation) * targetRotation);
		}

		private void DestroyLatchJoint()
		{
			// Joint
			Destroy(latchJoint);
			latchJoint = null;

			// for some rare cases
			vessel.ResetRBAnchor();
			if(otherPort) otherPort.vessel.ResetRBAnchor();
		}

		internal void UpdateDimension()
		{
			Transform Base000 = KSPUtil.FindInPartModel(transform, "Base.000");
			Transform Base001 = KSPUtil.FindInPartModel(transform, "Base.001");
			Transform Base002 = KSPUtil.FindInPartModel(transform, "Base.002");
			Transform Base003 = KSPUtil.FindInPartModel(transform, "Base.003");

			Vector3 Pos000 = Base000.localPosition;
			Pos000.x = length * 0.5f;
			Pos000.z = -1.334f - width * 0.5f;
			Base000.localPosition = Pos000;

			Vector3 Pos001 = Base001.localPosition;
			Pos001.x = -length * 0.5f;
			Pos001.z = -1.334f - width * 0.5f;
			Base001.localPosition = Pos001;

			Vector3 Pos002 = Base002.localPosition;
			Pos002.x = length * 0.5f;
			Pos002.z = 1.334f + width * 0.5f;
			Base002.localPosition = Pos002;

			Vector3 Pos003 = Base003.localPosition;
			Pos003.x = -length * 0.5f;
			Pos003.z = 1.334f + width * 0.5f;
			Base003.localPosition = Pos003;
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

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Length", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = 0.6f, maxValue = 8f),
			UI_FloatRange(minValue = 0.6f, maxValue = 8f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float length = 2f;

		private void onChanged_length(object o)
		{
			UpdateDimension();
		}

		[KSPAxisField(isPersistant = true, guiActive = false, guiActiveEditor = true, guiName = "Width", guiFormat = "F3",
			axisMode = KSPAxisMode.Incremental, minValue = -0.6f, maxValue = 0.6f),
			UI_FloatRange(minValue = -0.6f, maxValue = 0.6f, stepIncrement = 0.001f, suppressEditorShipModified = true, affectSymCounterparts = UI_Scene.All)]
		public float width = 0f;

		private void onChanged_width(object o)
		{
			UpdateDimension();
		}

		////////////////////////////////////////
		// Context Menu

		[KSPField(guiName = "Trunnion Port status", isPersistant = false, guiActive = true, guiActiveUnfocused = true, unfocusedRange = 20)]
		public string DockStatus = "Inactive";

		[KSPField(guiName = "Trunnion Port distance", isPersistant = false, guiActive = true)]
		public string DockDistance;

		[KSPField(guiName = "Trunnion Port angle", isPersistant = false, guiActive = true)]
		public string DockAngle;

		public void Enable()
		{
			fsm.RunEvent(on_enable);
		}

		public void Disable()
		{
			fsm.RunEvent(on_disable);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, guiName = "Deactivate End Effector")]
		public void TogglePort()
		{
			if(fsm.CurrentState == st_disabled)
				fsm.RunEvent(on_enable);
			else
				fsm.RunEvent(on_disable);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Latch")]
		public void Latch()
		{
			fsm.RunEvent(on_latching);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Release")]
		public void Release()
		{
			fsm.RunEvent(on_unlatching);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = false, guiName = "Dock")]
		public void Dock()
		{
			Debug.Log("Docking to vessel " + otherPort.vessel.GetDisplayName(), gameObject);

			dockedPartUId = otherPort.part.flightID;

			otherPort.otherPort = this;
			otherPort.dockedPartUId = part.flightID;

			DockingHelper.SaveCameraPosition(part);
			DockingHelper.SuspendCameraSwitch(10);

			if(otherPort.vessel == Vessel.GetDominantVessel(vessel, otherPort.vessel))
				DockingHelper.DockVessels(this, otherPort);
			else
				DockingHelper.DockVessels(otherPort, this);

			DockingHelper.RestoreCameraPosition(part);

			Destroy(latchJoint);
			latchJoint = null;

			fsm.RunEvent(on_dock);
			otherPort.fsm.RunEvent(otherPort.on_dock_passive);
		}

		[KSPEvent(guiActive = true, guiActiveUnfocused = true, externalToEVAOnly = true, unfocusedRange = 2f, guiName = "#autoLOC_6001445")]
		public void Undock()
		{
			Vessel oldvessel = vessel;
			uint referenceTransformId = vessel.referenceTransformId;

			DockingHelper.SaveCameraPosition(part);
			DockingHelper.SuspendCameraSwitch(10);

			DockingHelper.UndockVessels(this, otherPort);

			DockingHelper.RestoreCameraPosition(part);

			BuildLatchJoint();
			CalculateLatchJointTarget();

			otherPort.fsm.RunEvent(otherPort.on_undock_passive);
			fsm.RunEvent(on_undock);

			if(oldvessel == FlightGlobals.ActiveVessel)
			{
				if(vessel[referenceTransformId] == null)
					StartCoroutine(WaitAndSwitchFocus());
			}
		}

		public IEnumerator WaitAndSwitchFocus()
		{
			yield return null;

			DockingHelper.SaveCameraPosition(part);

			FlightGlobals.ForceSetActiveVessel(vessel);
			FlightInputHandler.SetNeutralControls();

			DockingHelper.RestoreCameraPosition(part);
		}

		////////////////////////////////////////
		// Actions

		[KSPAction("Enable")]
		public void EnableAction(KSPActionParam param)
		{ Enable(); }

		[KSPAction("Disable")]
		public void DisableAction(KSPActionParam param)
		{ Disable(); }

		[KSPAction("Dock", activeEditor = false)]
		public void DockAction(KSPActionParam param)
		{ Dock(); }

		[KSPAction("#autoLOC_6001444", activeEditor = false)]
		public void UndockAction(KSPActionParam param)
		{ Undock(); }

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
			vesselInfo =
				(dockInfo == null) ? null :
				((dockInfo.part == (IDockable)this) ? dockInfo.vesselInfo : dockInfo.targetVesselInfo);
		}

		// returns true, if the port is compatible with the other port
		public bool IsCompatible(IDockable otherPort)
		{
			if(otherPort == null)
				return false;

			ModuleTrunnionPins _otherPort = otherPort.GetPart().GetComponent<ModuleTrunnionPins>();

			if(!_otherPort)
				return false;

			if(!nodeTypesAcceptedS.Contains(_otherPort.nodeType)
			|| !_otherPort.nodeTypesAcceptedS.Contains(nodeType))
				return false;

			return true;
		}

		// returns true, if the port is (active and) ready to dock with an other (passive) port
		public bool IsReadyFor(IDockable otherPort)
		{
			if(otherPort != null)
			{
				if(!IsCompatible(otherPort))
					return false;
			}

			return (fsm.CurrentState == st_active);
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
			return "Trunnion Port";
		}

		string IModuleInfo.GetInfo()
		{
/*
			StringBuilder sb = new StringBuilder();
			sb.AppendFormat("Attach strength (catched): {0:F0}\n", catchedBreakForce);
			sb.AppendFormat("Attach strength (latched): {0:F0}\n", latchedBreakForce);

			if(electricChargeRequiredLatching != 0f)
			{
				sb.Append("\n\n");
				sb.Append("<b><color=orange>Requires:</color></b>\n");
				
				if(electricChargeRequiredLatching != 0f)
					sb.AppendFormat("- <b>Electric Charge:</b> {0:F0}\n  (for latching)", electricChargeRequiredLatching);
			}

			return sb.ToString();*/
return ""; // FEHLER, fehlt
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
