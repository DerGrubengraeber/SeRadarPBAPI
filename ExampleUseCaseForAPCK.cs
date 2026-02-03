
		string Ver = "1.1.295-R";
 
		// This is a version of TGP adapted to work with Nebulous Radar
		//
		// TGP Targeting Pod (Raycast tracking custom turret) (c)cheerkin
		// Steam WS: https://steamcommunity.com/sharedfiles/filedetails/?id=3158039679

		static int LOGGER_MAX_CHARS = 5000;

		// no toggle support yet
		Dictionary<string, string> keyUpBindings = new Dictionary<string, string>()
		{
			{ "a", "command:prev-target" },
			{ "d", "command:next-target" },
			{ "e", "command:cast" },
			{ "q", "command:drop-and-ban" },
			{ "w", "command:cycle-offset" },
			{ "s", "command:static-cast" }
		};
		Dictionary<string, string> dTapBindings = new Dictionary<string, string>()
		{
			{ "spacebar", "command:clear-user-shift" },
			{ "c", "command:reset-rotors" }
		};

		static class Variables
		{
			static Dictionary<string, ISettable> v = new Dictionary<string, ISettable> {
				{ "turret-target-min-size", new Variable<double> { value = 0f, parser = s => double.Parse(s) } },
				{ "raycast-range", new Variable<float> { value = 3000f, parser = s => float.Parse(s) } }, // this is used for manual forward-style casts, but not for maintaining the lock
				{ "tracking-clock", new Variable<int> { value = 1, parser = s => int.Parse(s) } },
				{ "squared-offset-filter", new Variable<float> { value = 16f, parser = s => int.Parse(s) } } // "resolution" for ai-turret-added target offsets, squared (16 is 4m)
			};
			public static void Set(string key, string value) { v[key].Set(value); }
			public static void Set<T>(string key, T value) { (v[key] as Variable<T>).value = value; }
			public static T Get<T>(string key) { return (v[key] as Variable<T>).value; }
			public interface ISettable
			{
				void Set(string v);
			}
			public class Variable<T> : ISettable
			{
				public T value;
				public Func<string, T> parser;
				public void Set(string v) { value = parser(v); }
			}
		}

		public enum TargetSelection { First, Closest, Random, Loop }
		public enum MultiTrackingSelector { Selected, AllOffsets, AllTargets, Everything }

		// further down code can be minified

		// TODO: rumors say static refs are not released on script recompile, refactor to instance

		class Toggle
		{
			Toggle() { }
			Action<string> onToggleStateChangeHandler;
			Dictionary<string, bool> sw;
			Toggle(Dictionary<string, bool> switches, Action<string> handler)
			{
				onToggleStateChangeHandler = handler;
				sw = switches;
			}

			public static Toggle C { get; private set; }

			public static void Init(Dictionary<string, bool> switches, Action<string> handler)
			{
				if (C == null)
					C = new Toggle(switches, handler);
			}

			public void Set(string key, bool value)
			{
				if (sw[key] != value)
					Invert(key);
			}
			public void Invert(string key)
			{
				sw[key] = !sw[key];
				onToggleStateChangeHandler(key);
			}
			public bool Check(string key)
			{
				return sw[key];
			}
			public ImmutableArray<MyTuple<string, string>> GetToggleCommands(string tmcNode)
			{
				return sw.Select(n => new MyTuple<string, string>($"{n.Key}: {(n.Value ? "on" : "off")}", $"[toggle:{n.Key}],[{tmcNode}]")).ToImmutableArray();
			}
		}

		public Program()
		{
			Runtime.UpdateFrequency = UpdateFrequency.Update1;

			Toggle.Init(new Dictionary<string, bool>
			{
				{ "pirate-scan", true },
				{ "sweep", false },
				{ "raycast-burst", true },
				{ "stabilize", true },
				{ "lock-friendlies", false },
			},
				key =>
				{
					switch (key)
					{
						
					}
				}
			);

			this.commandRegistry = new CommandRegistry(
				new Dictionary<string, Action<string[], object>> // <cmd>, <params, source>
					{
						{
							"cast", (parts, src) => {
								targeterRC.ForwardCast(null, parts.Length > 2 ? float.Parse(parts[2]) : 5f);
								TTM.TrackerTurrets.ForEach(x => x.ClearUserShift());
							}
						},
						{
							"static-cast", (parts, src) => {
								STPool.StaticCast(RCaster, MainCtrl, parts.Length > 2 ? int.Parse(parts[2]) : -1);
								TTM.TrackerTurrets.ForEach(x => x.ClearUserShift());
							}
						},
						{
							"cycle-offset", (parts, src) =>
							{
								var t = GetTurretOrTscTarget(src);
								if (t != null)
									t.SelectedOffsetIndex = Tom.CycleOffset(t.Info.EntityId, t.SelectedOffsetIndex);
							}
						},
						{
							"tom-src", (parts, src) => Tom?.CycleSource()
						},
						{
							"remove-offset", (parts, src) => {
								var t = GetTurretOrTscTarget(src);
								if (t != null)
									Tom.RemoveOffset(t.Info.EntityId, t.SelectedOffsetIndex);
							}
						},
						{
							"next-target", (parts, src) => Tsc.CycleTarget(TTPool.Targets, false)
						},
						{
							"prev-target", (parts, src) => Tsc.CycleTarget(TTPool.Targets, true)
						},
						{
							"save-offsets", (parts, src) => TORepo.SaveCurrent(parts, Tsc.SelectedTrackedTarget?.Info.EntityId)
						},
						{
							"set-target-def", (parts, src) => TORepo.SetTargetDef(parts)
						},
						{
							"cycle-selector", (parts, src) => {
								if (++GlobalSelector > MultiTrackingSelector.Everything)
										GlobalSelector = MultiTrackingSelector.Selected;
							}
						},
						{
							"set-selector", (parts, src) => Enum.TryParse(parts[2], out GlobalSelector)
						},
						{
							"set-offset-scan-designator-tag", (parts, src) => targeterTurret.ScanModeTurretTag = parts[2]
						},
						{
							"set-offset-scan-min-d", (parts, src) => targeterTurret.ScanModeMinD = long.Parse(parts[2])
						},
						{
							"set-offset-scan-min-size", (parts, src) => targeterTurret.ScanModeMinSize = long.Parse(parts[2])
						},
						{
							"set-filter-deny-inertial", (parts, src) => TGatingComp.DenyInertial = bool.Parse(parts[2])
						},
						{
							"set-filter-deny-smaller-than", (parts, src) => TGatingComp.DenySmallerThan = float.Parse(parts[2])
						},
						{
							"set-filter-deny-missing-sphere", (parts, src) => TGatingComp.DenyMissingSphere = float.Parse(parts[2])
						},
						{
							"set-filter-deny-wc-threat-lower-than", (parts, src) => TGatingComp.DenyWcThreatLowerThan = float.Parse(parts[2])
						},
						{
							"probe-surface", (parts, src) =>
								RCaster.ProbeSurfaceHandler(parts, (pos, norm) => {
									var nStr = VectorOpsHelper.V3DtoBroadcastString(norm).Replace(':', ';');
									var lTask = $"command:create-task:land:Name=Aligned Landing,AimNormal={nStr}:{VectorOpsHelper.V3DtoBroadcastString(pos)}";
									UnicastCommandAutoPillock(lTask);
								})
						},
						{
							"recycle", (parts, src) => Recycle() // reset-trackers
						},
						{
							"reset-rotors", (parts, src) => TTM.TrackerTurrets.ForEach(x => x.RequestReset())
						},
						{
							"clear-user-shift", (parts, src) =>
							{
								if (src is TrackerTurret)
								{
									((TrackerTurret)src).ClearUserShift();
								}
							}
						},
						{
							"drop", (parts, src) => DropTarget(src, false)
						},
						{
							"drop-and-ban", (parts, src) => DropTarget(src, true)
						},
						{
							"clear-bans", (parts, src) => TGatingComp.ClearBans()
						},
						{
							"set-value", (parts, src) => Variables.Set(parts[2], parts[3])
						},
            {
              "allow-target-relation-radar", (parts, src) => targeterRadar?.AllowRadarRelation(parts[2])
            },
						{
							"allow-target-relation", (parts, src) => targeterWc?.AllowWcRelation(parts[2])
						},
						{
							"refresh-designators", (parts, src) =>
							{
								var localTurrets = new List<IMyLargeTurretBase>();
								GridTerminalSystem.GetBlocksOfType(localTurrets, x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("tgp"));
								var localCTC = new List<IMyTurretControlBlock>();
								GridTerminalSystem.GetBlocksOfType(localCTC, x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("tgp"));
								targeterTurret.AddDesignators(localTurrets, localCTC);
							}
						},
						{
							"get-toggles", (parts, src) => {
								var tmcNode = string.Join(":", parts.Take(3));
								IGC.SendUnicastMessage(long.Parse(parts[2]),
								$"menucommand.get-commands.reply:{ tmcNode }",
								Toggle.C.GetToggleCommands(tmcNode));
							}
						},
					}
				);
		}

		void DropTarget(object src, bool ban)
		{
			long? id = null;
			if (src is TrackerTurret)
				id = ((TrackerTurret)src).GetFocusedTargetId();

			TrackedTarget toRemove = null;
			if ((id == null) && (Tsc.SelectedTrackedTarget != null))
				toRemove = Tsc.SelectedTrackedTarget;
			else
				toRemove = TTPool.GetById(id ?? -1);

			if (toRemove != null)
			{
				toRemove.MarkedForRemoval = true;
				TTM.TrackerTurrets.ForEach(x => x.ClearUserShift());
				if (ban)
					TGatingComp.BanPermanent(toRemove.Info.EntityId);
			}
		}

		TrackedTarget GetTurretOrTscTarget(object src)
		{
			if (src is TrackerTurret)
			{
				var id = ((TrackerTurret)src).GetFocusedTargetId();
				if (id.HasValue)
					return TTPool.GetById(id.Value);
			}
			else if (Tsc.SelectedTrackedTarget != null)
				return Tsc.SelectedTrackedTarget;
			return null;
		}

		static void AddUniqueItem<T>(T item, IList<T> c) where T : class
		{
			if ((item != null) && !c.Contains(item))
				c.Add(item);
		}

		IMyBlockGroup GetThisBlockGroup(IMyTerminalBlock block)
		{
			List<IMyBlockGroup> groups = new List<IMyBlockGroup>();
			GridTerminalSystem.GetBlockGroups(groups);
			return groups.Where(g => {
				var bs = new List<IMyTerminalBlock>();
				g.GetBlocksOfType(bs);
				return bs.Contains(block);
			}).FirstOrDefault();
		}

		List<T> GetCoreC<T>(List<IMyTerminalBlock> set, string n = null) where T : class, IMyTerminalBlock
		{
			E.Echo($"Looking for blocks ({typeof(T).Name})");
			var f = set.Where(b => b is T && ((n == null) || b.CustomName.Contains(n))).Cast<T>().ToList();
			return f;
		}

		void Recycle()
		{
			var gr = GetThisBlockGroup(Me);
			if (gr == null)
			{
				Runtime.UpdateFrequency = UpdateFrequency.None;
				Echo("Can't find hardware group containing this PB, stopping now.");
			}
			else
			{
				var blocks = new List<IMyTerminalBlock>();
				gr.GetBlocks(blocks);

				var controllers = GetCoreC<IMyShipController>(blocks);
				UserCtrlListener = new InputListener(controllers, () => E.T);

				// required
				MainCtrl = controllers.FirstOrDefault(x => x.CubeGrid == Me.CubeGrid);
				if (MainCtrl == null)
					throw new Exception("Need a ship controller on the same grid/group as the PB");

				// optional
				var cams = GetCoreC<IMyCameraBlock>(blocks);
				RCaster = new Raycaster(cams);

				var tTurrets = GetCoreC<IMyLargeTurretBase>(blocks);
				var tControllers = GetCoreC<IMyTurretControlBlock>(blocks);

				var localTurrets = new List<IMyLargeTurretBase>();
				GridTerminalSystem.GetBlocksOfType(localTurrets, x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("tgp"));
				tTurrets.AddRange(localTurrets);

				var localCTC = new List<IMyTurretControlBlock>();
				GridTerminalSystem.GetBlocksOfType(localCTC, x => x.CubeGrid == Me.CubeGrid && x.CustomName.Contains("tgp"));
				tControllers.AddRange(localCTC);

				TORepo = new TargetOffsetRepo(Tom, s => Storage = s);
				TORepo.LoadFromStorage(Storage); // TODO: defer
				Tom.OnAddedOffset += TORepo.ConsiderOffset;
				TORepo.OnDefinitionMatched += Tom.ReplaceOffsetsForEntity;

				TGatingComp = new TargetGatingComponent(MainCtrl);

				Tsc = new TargetSelectorComponent(TORepo);

				TTPool = new TrackedTargetsPool(TGatingComp);

				targeterRC = new RaycastTrackerTargeter(RCaster, MainCtrl, Tom, Tsc, TTPool);

				TTM = new TrackingTurretManager(MainCtrl, Tom);

				var allStators = GetCoreC<IMyMotorStator>(blocks);
				foreach (var trCam in cams.Where(x => x.CustomName.Contains("tracker")))
				{
					var topGrid = trCam.CubeGrid;
					E.Echo($"Getting RC for tracker cam: {trCam.CustomName}");
					var rc = GetCoreC<IMyRemoteControl>(blocks).Where(x => x.CubeGrid == topGrid).Single();
					var stators = new List<IMyMotorStator>();
					var stator1 = allStators.Where(x => x.TopGrid == topGrid).Single();
					stators.Add(stator1);
					var stator2 = allStators.Where(x => x.TopGrid == stator1.CubeGrid).Single();
					if (stator2 != null)
						stators.Add(stator2);
					var turr = new TrackerTurret(stators, rc, trCam, UserCtrlListener, commandRegistry, keyUpBindings, dTapBindings);
					turr.RequestReset();
					TTM.TrackerTurrets.Add(turr);
				}

				targeterRC.OnCamRcSuccess = (cam, id, p) =>
				{
					foreach (var tracker in TTM.TrackerTurrets)
					{
						if ((tracker.Cam == cam) || (cam == null) && tracker.FacingPosition(p))
						{
							if (tracker.GetFocusedTargetId() != id)
								tracker.FocusedTargetIds.Push(id);
							break;
						}
					}
				};

				targeterTurret = new TurretAiTargeter(Tom, TTPool);
				targeterTurret.AddDesignators(tTurrets, tControllers);
				TORepo.OnDefinitionMatched += (id, offs) => targeterTurret.StopScan(id);
				// redundant since hybrid target tracking?
				targeterTurret.OnTargetEncountered += targeterRC.CheckEncounter;

				caster = new TargetCasterP2P(IGC, processedTargets);

				List<IMyProgrammableBlock> pbs = GetCoreC<IMyProgrammableBlock>(blocks);
				outputSvcPb = pbs.FirstOrDefault(b => b.CustomName.Contains("a-hud-svc") && b.IsSameConstructAs(Me));
				APckCoreId = pbs.FirstOrDefault(b => (b.CustomName.Contains("core") || b.CustomData.Contains("pillock-mode")) && b.IsSameConstructAs(Me))?.EntityId;

				Targeters = new List<ITgpTargeter> { targeterRC, targeterTurret };

				var dict = Me.GetProperty("WcPbAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(Me);
				if (dict?.ContainsKey("GetSortedThreatsByID") == true)
				{
					E.DebugLog("WC detected, disabling ITgpTargeter and TurretAiTargeter instances.");
					//Targeters.Clear(); // TODO: is this still needed?
					var wcDataProvider = dict["GetSortedThreatsByID"] as Action<IMyTerminalBlock, IDictionary<long, MyDetectedEntityInfo>>;
					if (wcDataProvider != null)
					{
						E.DebugLog("Creating WcTargeter.");
						targeterWc = new WcTargeter(TTPool, Tom, wcDataProvider, Me);
						Targeters.Add(targeterWc);
					}
				}

		dict = Me.GetProperty("GrubenRadarAPI")?.As<IReadOnlyDictionary<string, Delegate>>().GetValue(Me);
        if(dict?.ContainsKey("GetDetectedRadarTargets") == true)
        {
          E.DebugLog("Nebulous Radar detected.");
          var radarDataProvider = dict["GetDetectedRadarTargets"] as Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>>;
          if (radarDataProvider != null)
          {
            E.DebugLog("Creating RadarTargeter.");
            targeterRadar = new RadarTargeter(TTPool, Tom, radarDataProvider, Me);
            Targeters.Add(targeterRadar);
          }
        }

			}

			if (!string.IsNullOrEmpty(Me.CustomData))
				pendingInitSequence = true;
		}

		// ctor ends
		bool constructed;
		bool pendingInitSequence;

		IMyProgrammableBlock outputSvcPb;
		long? APckCoreId;

		IMyShipController MainCtrl;

		CommandRegistry commandRegistry;
		public class CommandRegistry
		{
			Dictionary<string, Action<string[], object>> commands;
			public CommandRegistry(Dictionary<string, Action<string[], object>> commands)
			{
				this.commands = commands;
			}
			public void RunCommand(string id, string[] cmdParts, object source = null)
			{
				E.DebugLog($"Got cmd: {id}");
				this.commands[id].Invoke(cmdParts, source);
			}
		}

		TrackingTurretManager TTM;
		public class TrackingTurretManager
		{
			public List<TrackerTurret> TrackerTurrets = new List<TrackerTurret>();
			IMyShipController _ctrl;
			TargetOffsetManager _tom;

			public TrackingTurretManager(IMyShipController ctrl, TargetOffsetManager tom)
			{
				_ctrl = ctrl;
				_tom = tom;
			}

			public void HandleTick()
			{
				Vector3D av  = Toggle.C.Check("stabilize") ? _ctrl.GetShipVelocities().AngularVelocity : Vector3D.Zero;
				foreach (var t in TrackerTurrets)
				{
					t.HandleTick(ref av);
				}
			}

			public void OnTargetRootUpdated(TrackedTarget t)
			{
				for (int n = 0; n < TrackerTurrets.Count; n++)
				{
					if (TrackerTurrets[n].GetFocusedTargetId() == t.Info.EntityId)
					{
						TrackerTurrets[n].FocusedTargetPos = _tom.GetOffset(t.Info.EntityId, t.SelectedOffsetIndex, t.Info.Orientation, t.PredictedPosition);
						return;
					}
				}
			}

			public void OnTargetRootRemoved(TrackedTarget tt)
			{
				for (int n = 0; n < TrackerTurrets.Count; n++)
				{
					if (TrackerTurrets[n].GetFocusedTargetId() == tt.Info.EntityId)
					{
						E.DebugLog($"TrackingTurretManager: Target root {tt.Info.EntityId} removed");
						TrackerTurrets[n].FocusedTargetPos = null;
						TrackerTurrets[n].FocusedTargetIds.Pop();
						return;
					}
				}
			}
		}

		public class TrackerTurret
		{
			IMyRemoteControl _rc;
			List<IMyMotorStator> _rotors;
			InputListener _il;
			public IMyCameraBlock Cam { get; private set; }
			CommandRegistry _cmdR;
			float[] prevCV;
			Dictionary<string, string> _keyUpBindings;
			Dictionary<string, string> _dTapBindings;

			public TrackerTurret(List<IMyMotorStator> r, IMyRemoteControl rc, IMyCameraBlock cam, InputListener il, 
					CommandRegistry cmdR, Dictionary<string, string> keyUpBindings, Dictionary<string, string> dTapBindings)
			{
				_rotors = r;
				_rc = rc;
				_il = il;
				Cam = cam;
				prevCV = new float[r.Count];
				_cmdR = cmdR;
				_keyUpBindings = keyUpBindings;
				_dTapBindings = dTapBindings;
			}

			//public long? FocusedTargetId;
			public Stack<long> FocusedTargetIds = new Stack<long>();
			public Vector3D? FocusedTargetPos;

			public long? GetFocusedTargetId()
			{
				if (FocusedTargetIds.Count > 0)
					return FocusedTargetIds.Peek();
				return null;
			}

			public bool FacingPosition(Vector3D p)
			{
				return Vector3D.Dot(Cam.WorldMatrix.Forward, Vector3D.Normalize(p - Cam.WorldMatrix.Translation)) > 0.71f;
			}

			public void HandleTick(ref Vector3D angVel)
			{
				if (_resettingTstamp != 0)
					Reset();
				else
				{
					if (FocusedTargetPos.HasValue)
						AimTo(FocusedTargetPos.Value);
					else
					{
						var ctrl = angVel;

						//E.Echo($"ctrl: {ctrl}");

						if (_rc.IsUnderControl)
						{
							HandleWasd();
							var r = _il.GetRot();
							var wR = Vector3D.Rotate(new Vector3D(r.X, r.Y, 0), _rc.WorldMatrix);
							//E.Echo($"wR: {wR}");
							ctrl += wR / 10;
							//E.Echo($"ctrl2: {ctrl}");
						}

						if (ctrl != Vector3D.Zero)
						{
							foreach (var stator in _rotors)
							{
								var inv = MatrixD.Transpose(stator.WorldMatrix);
								stator.TargetVelocityRad = (float)Vector3D.Rotate(ctrl, inv).Y;
							}
						}
						else
						{
							foreach (var stator in _rotors)
								stator.TargetVelocityRad = 0;
						}
					}

					//if (FocusedTargetPos.HasValue)
					//	AimTo(FocusedTargetPos.Value);
					//else
					//	RotateByInput();
				}
			}

			void AimTo(Vector3D tPos)
			{
				var toTarget = tPos - Cam.GetPosition();

				if (_rc.IsUnderControl)
				{
					HandleWasd();

					var r = _il.GetRot();
					_cumulative += r;
					var adj = _cumulative;

					float maxF = 1000;
					Func<float, float> constrain = x =>
					{
						x = Math.Min(Math.Abs(x), maxF) * Math.Sign(x);
						return x;
					};

					adj.X = constrain(adj.X);
					adj.Y = constrain(adj.Y);

					// we don't want the indicator to float away too far
					_cumulative = adj;
					// adjusted to degree max
					//adj = adj / maxF;
					adj = adj / 4;

					//toTarget += _cam.WorldMatrix.Down * adj.X + _cam.WorldMatrix.Right * adj.Y;

					// 0,785398 - 45 deg
					var q1 = QuaternionD.CreateFromAxisAngle(_rc.WorldMatrix.Down, adj.Y / 250 * 0.785);
					var q2 = QuaternionD.CreateFromAxisAngle(_rc.WorldMatrix.Left, adj.X / 250 * 0.785);
					toTarget = Vector3D.Transform(toTarget, q1 * q2);
				}

				ControlRotors(toTarget);
			}

			Vector3D? fwCapture;

			void RotateByInput()
			{
				//if (_rc.IsUnderControl)
				//{
				//	HandleWasd();

				//	var r = _il.GetRot();

				//	var wm = Cam.WorldMatrix;
				//	if (fwCapture.HasValue)
				//	{
				//		Func<double, double> adjust = (x) => Math.Sign(x) * Math.Pow(x, 2);
				//		r.X = (float)adjust(r.X / 10);
				//		r.Y = (float)adjust(r.Y / 10);
				//		var fwUpdated = Vector3D.Normalize(fwCapture.Value * 1000 + wm.Down * r.X + wm.Right * r.Y);
				//		fwCapture = fwUpdated;
				//		ControlRotors(fwCapture.Value * 1000 + wm.Down * r.X + wm.Right * r.Y, false);
				//	}
				//	else
				//	{
				//		Func<double, double> adjust = (x) => Math.Sign(x) * Math.Pow(x, 2);
				//		ControlRotors(wm.Forward * 1000 + wm.Down * adjust(r.X) + wm.Right * adjust(r.Y), false);
				//	}
				//}
			}

			void HandleWasd()
			{
				foreach (var keyUp in _keyUpBindings)
				{
					if (_il.KeyReleased(keyUp.Key))
					{
						string[] cmdParts = keyUp.Value.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
						_cmdR.RunCommand(cmdParts[1], cmdParts, this);
					}
				}
				foreach (var dTap in _dTapBindings)
				{
					if (_il.CheckDoubleTap(dTap.Key))
					{
						string[] cmdParts = dTap.Value.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
						_cmdR.RunCommand(cmdParts[1], cmdParts, this);
					}
				}
			}

			Vector2 _cumulative;
			int _resettingTstamp;
			public void Reset()
			{
				E.Echo($"Reset: {E.T - _resettingTstamp}");
				if (E.T - _resettingTstamp < 120)
				{
					foreach (var r in _rotors)
					{
						r.UpperLimitRad = 0;
						r.LowerLimitRad = 0;
						//r.TargetVelocityRPM = r.Angle > 3.14 ? 60 : -60;
						//var v = GetRotorAngleStatorZeroBased(r, false, r.WorldMatrix.Backward);
						//r.TargetVelocityRad = Math.Sign(v) * 60;
						if (r.Angle > 0 && r.Angle < Math.PI)
							r.TargetVelocityRPM = -60;
						else
							r.TargetVelocityRPM = 60;
						//r.TargetVelocityRPM = 60;
					}
				}
				else
				{
					foreach (var r in _rotors)
					{
						r.UpperLimitRad = float.MaxValue;
						r.LowerLimitRad = float.MinValue;
						r.TargetVelocityRPM = 0;
					}
					_cumulative = Vector2.Zero;
					_resettingTstamp = 0;
					FocusedTargetIds.Clear();
					FocusedTargetPos = null;
				}
			}

			public void RequestReset()
			{
				_resettingTstamp = E.T + 1;
			}

			public void ClearUserShift()
			{
				if (GetFocusedTargetId() != null)
					_cumulative = Vector2.Zero;
				else
				{
					if (!fwCapture.HasValue)
					{
						fwCapture = Cam.WorldMatrix.Forward;
					}
					else
					{
						fwCapture = null;
					}
				}
			}

			void ControlRotors(Vector3D toTarget, bool adjustRotorSpeed = true)
			{
				int rotorIndex = 0;
				foreach (var stator in _rotors)
				{
					var correction = GetRotorAngleStatorZeroBased(stator, false, toTarget);

					var AlignMag = Math.Abs(correction);
					var deltaS = AlignMag - prevCV[rotorIndex];

					var maxFactor = 2.5f;
					var curv = 2f;
					Func<double, double, double> ampF = (x, d) => x * (Math.Exp(-d * curv) + 0.8) * maxFactor / 2;

					if ((deltaS < 0.035) && (AlignMag > 0.0175)) // 2 and 1 grad
					{
						var raw = correction;
						correction = (float)ampF(correction, deltaS);
					}

					prevCV[rotorIndex] = AlignMag;
					rotorIndex++;

					if (float.IsNaN(correction))
					{
						continue;
					}

					float vel = correction;
					var mag = Math.Abs(correction);
					int gyroRPMlimit = 30;
					double RPMtoRadsSec = 2 * Math.PI / 60f;
					var radsPerTickMax = gyroRPMlimit * RPMtoRadsSec;

					if (adjustRotorSpeed)
					{
						float intertLagC = 7; // 7
						Func<double, double> rpmLimitSimple = x =>
						{
							var m = Math.Abs(x); // rads
							if (m < 0.15)
								intertLagC = 1f;
							else if (m < 0.2)
								intertLagC = 1f;
							else if (m < 0.25)
								intertLagC = 4f;
							return m > radsPerTickMax * intertLagC ? gyroRPMlimit : m / (radsPerTickMax * intertLagC) * gyroRPMlimit;
						};
						vel = Math.Sign(correction) * (float)rpmLimitSimple(mag);
					}

					stator.TargetVelocityRad = vel;
					stator.Enabled = true;
				}
			}

			void AlignVtolRotors(List<IMyMotorStator> rotors, Vector3D toTarget, int clock)
			{
				//foreach (var stator in rotors)
				//{
				//	var correction = GetRotorAngleStatorZeroBased(stator, false, toTarget);
				//	E.Echo($"{stator.CustomName} err: {correction:f4}");
				//	var mag = Math.Abs(correction);
				//	int gyroRPMlimit = 30;
				//	float adjustmentsPerSec = 60f / clock;
				//	float RPMtoRadsSec = 2 * (float)Math.PI / 60f;
				//	var radsPerTickMax = gyroRPMlimit * RPMtoRadsSec / clock;

				//	float intertLagC = Variables.Get<float>("inertia-gyro-divisor");
				//	Func<double, double> rpmLimitSimple = x =>
				//	{
				//		var m = Math.Abs(x); // rads
				//		return Math.Min(radsPerTickMax, x);
				//	};

				//	var vel = Math.Sign(correction) * (float)rpmLimitSimple(mag) / RPMtoRadsSec;

				//	stator.TargetVelocityRad = vel;
				//	stator.Enabled = true;
				//	stator.RotorLock = false;
				//}
			}

			float GetRotorAngleStatorZeroBased(IMyMotorStator stator, bool hinge, Vector3D toTarget)
			{
				var planeNorm = hinge ? stator.WorldMatrix.Up : stator.WorldMatrix.Up;
				var zeroAngeDir = hinge ? stator.WorldMatrix.Left : stator.WorldMatrix.Backward;

				var proj = Vector3D.ProjectOnPlane(ref toTarget, ref planeNorm);
				var angle = -(float)Math.Atan2(Vector3D.Dot(Vector3D.Cross(zeroAngeDir, proj), planeNorm),
					Vector3D.Dot(zeroAngeDir, proj));

				// 0-360
				Func<float, float> roundAngle = a =>
				{
					while (a > 2 * (float)Math.PI)
						a -= 2 * (float)Math.PI;

					if (a < 0)
						a = 2 * (float)Math.PI + a;
					return a;
				};

				var diff = roundAngle(angle) - roundAngle(stator.Angle); // -6
				var vel = (Math.Abs(diff) < Math.PI) ? diff : -diff; // v = 6
				if (Math.Abs(vel) > Math.PI)
					vel = -(Math.Abs(vel) - 2 * (float)Math.PI) * Math.Sign(vel);

				return vel;
			}


		}

		List<IncomingMessage> batch = new List<IncomingMessage>();
		List<MyIGCMessage> uniMsgs = new List<MyIGCMessage>();
		void Main(string arg)
		{
			E.T++;
			E.Dt = Math.Max(0.001, Runtime.TimeSinceLastRun.TotalSeconds);

			// because ctr runs when PB comes into existance, even if turned off
			if (!constructed)
			{
				E.Init(Echo, GridTerminalSystem, Me, IGC);
				// because WC
				if (E.T > 60)
				{
					Recycle();
					constructed = true;
				}
				else
				{
					E.Echo("Waiting for init" + new string('.', E.T / 10));
					return;
				}
			}

			E.InfoOnTickStart?.Invoke();
			E.LockOnTickStart?.Invoke();
			E.Info?.Invoke(E.T + "");
			UserCtrlListener.PrepareBeforeTick();
			E.Echo($"TGP v.{Ver}");
			E.Echo($"Selector: {GlobalSelector}");
			E.Echo($"SelectedTarget: {Tsc.SelectedTrackedTarget?.Info.EntityId}");

			TGatingComp.UpdateBefore();

			batch.Clear();

			try
			{
				uniMsgs.Clear();
				while (IGC.UnicastListener.HasPendingMessage)
				{
					uniMsgs.Add(IGC.UnicastListener.AcceptMessage());
				}
				foreach (var m in uniMsgs)
				{
					metrics.RcvMsg++;
					if (m.Tag == "apck.command")
						batch.AddRange(ParseMessage(m.Data.ToString(), m.Source).ToList());
					if (m.Tag == "apck-handshake")
					{
						IGC.SendUnicastMessage(m.Source, "apck-handshake-reply", "TGP");
						APckCoreId = m.Source;
					}
					if (m.Tag == "apck-encounter")
					{
						var d = (MyTuple<long, Vector3D>)m.Data;
						targeterRC.CheckEncounter(d.Item1, d.Item2);
					}
				}

				if (pendingInitSequence && string.IsNullOrEmpty(arg))
				{
					pendingInitSequence = false;
					var cmds = Me.CustomData.Trim('\n').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Where(s => !s.StartsWith("//")).Select(s => "[" + s + "]").ToList();
					cmds.AddRange(Storage.Trim('\n').Split(new[] { '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(s => "[" + s + "]").ToList());
					arg = string.Join(",", cmds);
				}

				if (!string.IsNullOrEmpty(arg) && arg.Contains(":"))
				{
					metrics.RcvMsg++;
					batch.AddRange(ParseMessage(arg).ToList());
				}

				if (batch.Count > 0)
				{
					foreach (var incomingMessage in batch)
					{
						string[] cmdParts = incomingMessage.Msg.Split(new[] { ':' }, StringSplitOptions.RemoveEmptyEntries);
						if (cmdParts[0] == "toggle")
						{
							Toggle.C.Invert(cmdParts[1]);
						}
						if (cmdParts[0] == "command")
						{
							commandRegistry.RunCommand(cmdParts[1], cmdParts);
						}
					}
				}

				// update grid sense - gets GNP/tDesignator/Raycast
				HandleGridSense();
				E.Echo($"Targets available {TTPool.Targets.Count + STPool.Count} selected {processedTargets.Count}");
				caster.Handle(uniMsgs);

				TTM.HandleTick();
			}
			catch (Exception ex)
			{
				Runtime.UpdateFrequency = UpdateFrequency.None;
				if (outputSvcPb != null)
					IGC.SendUnicastMessage(outputSvcPb.EntityId, "persist-text",
						new MyTuple<string, Vector2, float>(
						$"TGP FAILURE see \"{MainCtrl.CustomName}\" CustomData",
						new Vector2(0f, 0.4f),
						0.8f
					));
				MainCtrl.CustomData = ex.ToString();
				E.DebugLog(ex.ToString());
				throw ex;
			}

			E.Echo($"Tracker turrets: {TTM.TrackerTurrets.Count}");
			E.Echo($"CTC/turret designators: {targeterTurret.Count}");

			E.Echo("ParseVectorsCount: " + metrics.ParseVectorsCount);
			E.Echo("ScanBurstsCount: " + metrics.ScanBurstsCount);

			RCaster.UpdateAfter(E.T);
			STPool.UpdateAfter(E.T);
			STPool.Sweep(MainCtrl, RCaster, Runtime, TTPool);
			E.Echo($"Total range: {RCaster.Resource / 1000:f2} km");
			E.Echo("Total cams: " + RCaster.CamsCount);

			Scheduler.C.HandleTick();
			E.EndOfTick();

			E.Echo($"CurrentInstructionCount: {Runtime.CurrentInstructionCount}");
			E.Echo("Processed in " + Runtime.LastRunTimeMs.ToString("f3") + " ms");
		}


		TargetCasterP2P caster;
		public class TargetCasterP2P
		{
			IMyIntergridCommunicationSystem _igc;
			IMyBroadcastListener _b;
			IMyBroadcastListener _bWho;
			IMyBroadcastListener _bWhoPredicate;
			List<TargetLite> _targets;

			public TargetCasterP2P(IMyIntergridCommunicationSystem igc, List<TargetLite> tgts)
			{
				_igc = igc;
				_b = igc.RegisterBroadcastListener("apck.unicast.closed"); // key
				_bWho = igc.RegisterBroadcastListener("apck.unicast.whohas"); // key, dataId
				_bWhoPredicate = igc.RegisterBroadcastListener("apck.unicast.whohas+predicate"); // key, TargetSelection, pos, dir, radius, callback
				_targets = tgts;
				
				Init();
			}

			Dictionary<long, HashSet<long>> _subscriptions = new Dictionary<long, HashSet<long>>();

			List<TargetLite> _selectionBuffer = new List<TargetLite>();
			Random _r = new Random();
			int _ctr;

			List<MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>> _broadRangeBuffer = 
					new List<MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>>();

			Dictionary<long, List<MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>>> _subBatchBuffer =
				new Dictionary<long, List<MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>>>();

			List<MyTuple<long, Vector3D>> _offersBuffer = new List<MyTuple<long, Vector3D>>();

			void Init()
			{
				var tgp = new UniSubscribeProxy();
				tgp.AddSub = (src, id) =>
				{
					if (!_subscriptions.ContainsKey(id))
						_subscriptions.Add(id, new HashSet<long>());
					_subscriptions[id].Add(src);
				};
				tgp.RemoveSub = (src, id) =>
				{
					if (id == 0)
					{
						E.DebugLog($"Removing all subscriptions for consumer {src}");
						foreach (var s in _subscriptions)
							s.Value.Remove(src);
					}
					else
					{
						E.DebugLog($"Removing consumer {src} for target {id}");
						if (_subscriptions.ContainsKey(id))
							_subscriptions[id].Remove(src);
					}
				};
				tgp.WhoHas = (id) => _targets.Any(x => x.Id == id);

				tgp.WhoHasFiltered = (typeF, s, p, dir, r) =>
				{
					var ts = (TargetSelection)s;
					_selectionBuffer.Clear();
					foreach (var t in _targets)
					{
						bool locPass = t.Position.HasValue && FilterLoc(p, dir, r, t.Position.Value);
						if (locPass && (typeF == null || (t.Mdei.Value.Type.ToString() == typeF)))
							_selectionBuffer.Add(t);
					}
					if (_selectionBuffer.Count > 0)
					{
						if (ts == TargetSelection.Closest)
							return _selectionBuffer.OrderBy(x => (p - x.Position.Value).LengthSquared()).First().Id;
						else
						{
							int i = 0;
							if (ts == TargetSelection.Random)
								i = _r.Next(_selectionBuffer.Count);
							if (ts == TargetSelection.Loop)
								i = _ctr++ % _selectionBuffer.Count;
							return _selectionBuffer[i].Id;
						}
					}
					return -1;
				};

				tgp.Send = () =>
				{
					_broadRangeBuffer.Clear();
					foreach (var list in _subBatchBuffer.Values) // TODO: slight leak by not cleaning on unsub
						list.Clear();
					_offersBuffer.Clear();

					foreach (var tt in _targets)
					{
						_offersBuffer.Add(new MyTuple<long, Vector3D>(tt.Id, tt.Position.Value));
						if (_subscriptions.ContainsKey(tt.Id))
						{
							var subs = _subscriptions[tt.Id];
							if (subs.Count > 0)
							{
								var dto = tt.GetIgcDto();
								foreach (var s in subs)
								{
									if (!_subBatchBuffer.ContainsKey(s))
										_subBatchBuffer.Add(s, new List<MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>>());
									_subBatchBuffer[s].Add(dto);
								}
							}
							else
								_subscriptions.Remove(tt.Id);
						}

						// TODO: avoid if no active listener available
						// this is for Commander exclusively as it does not do target+ subscription yet
						_broadRangeBuffer.Add(tt.GetIgcDto());
					}

					foreach (var pair in _subBatchBuffer)
					{
						if (pair.Value.Count > 1)
							_igc.SendUnicastMessage(pair.Key, "tgp.local.gridsense.update", pair.Value.ToImmutableArray());
						else if (pair.Value.Count > 0)
							_igc.SendUnicastMessage(pair.Key, "tgp.local.gridsense.update", pair.Value[0]);
					}

					if (_offersBuffer.Count > 1)
						_igc.SendBroadcastMessage("tgp.global.gridsense.offer", _offersBuffer.ToImmutableArray());
					else
						foreach (var targetOffer in _offersBuffer)
							_igc.SendBroadcastMessage("tgp.global.gridsense.offer", targetOffer);
					

					// TODO: avoid if no active listener available
					// this is for Commander exclusively as it does not do target+ subscription yet
					if (_broadRangeBuffer.Count > 0)
						_igc.SendBroadcastMessage("tgp.global.gridsense.batch", _broadRangeBuffer.ToImmutableArray());
				};

				_proxies.Add("tgp.local.gridsense.update", tgp);
			}
			public void Handle(List<MyIGCMessage> msgs)
			{
				HandleProxies(msgs);
			}

			public class UniSubscribeProxy
			{
				public Action<long, long> RemoveSub;
				public Func<long, bool> WhoHas;
				public Func<string, byte, Vector3D, Vector3D, float, long> WhoHasFiltered;
				public Action<long, long> AddSub;
				public Action Send;
			}

			Dictionary<string, UniSubscribeProxy> _proxies = new Dictionary<string, UniSubscribeProxy>();

			public static bool FilterLoc(Vector3D p, Vector3D dir, float r, Vector3D tP)
			{
				bool b = false;
				if (dir != Vector3D.Zero)
				{
					if (Vector3D.Dot(dir, Vector3D.Normalize(tP - p)) > r)
						b = true;
				}
				else
				{
					if (p != Vector3D.Zero)
					{
						if (Vector3D.DistanceSquared(tP, p) < r * r)
							b = true;
					}
					else
						b = true;
				}
				return b;
			}

			public void HandleProxies(List<MyIGCMessage> msgs)
			{
				while (_b.HasPendingMessage)
				{
					var m = _b.AcceptMessage();
					var sKey = (string)m.Data;
					// can be used in many emitters and the stuff is not guaranteed, while closer channel is shared
					if (_proxies.ContainsKey(sKey))
						_proxies[sKey].RemoveSub.Invoke(m.Source, 0);
				}

				while (_bWho.HasPendingMessage)
				{
					var m = _bWho.AcceptMessage();
					var d = (MyTuple<string, long>)m.Data;
					if (_proxies.ContainsKey(d.Item1) && _proxies[d.Item1].WhoHas.Invoke(d.Item2))
						_igc.SendUnicastMessage(m.Source, "apck.unicast.ihave", d);
				}

				while (_bWhoPredicate.HasPendingMessage)
				{
					var m = _bWhoPredicate.AcceptMessage();
					// channelKey, callback, typeFilter, selector, pos, dir, r
					var d = (MyTuple<string, string, MyTuple<string, byte, Vector3D, Vector3D, float>>)m.Data;
					if (_proxies.ContainsKey(d.Item1) && _proxies[d.Item1].WhoHasFiltered != null)
					{
						E.DebugLog($"{d.Item1}: whohas with predicate");
						var id = _proxies[d.Item1].WhoHasFiltered.Invoke(d.Item3.Item1, d.Item3.Item2, d.Item3.Item3, d.Item3.Item4, d.Item3.Item5);
						if (id > 0)
						{
							E.DebugLog($"Offering target {id} to {m.Source} with callback {d.Item2}");
							_igc.SendUnicastMessage(m.Source, "apck.unicast.ihave+callback", new MyTuple<long, string>(id, d.Item2));
						}
					}
				}

				foreach (var m in msgs)
				{
					if (m.Tag == "apck.unicast.t+.batch")
					{
						foreach (var sub in (ImmutableArray<MyTuple<string, long>>)m.Data)
							UpdateSub(false, m.Source, sub);
					}
					else if (m.Tag.Contains("apck.unicast.t"))
					{
						var d = (MyTuple<string, long>)m.Data;
						if (m.Tag == "apck.unicast.t+")
							UpdateSub(false, m.Source, d);
						if (m.Tag == "apck.unicast.t-")
							UpdateSub(true, m.Source, d);
					}
				}

				foreach (var x in _proxies)
					x.Value.Send();
			}

			void UpdateSub(bool remove, long src, MyTuple<string, long> d)
			{
				E.DebugLog($"{src} {(remove ? "t-" : "t+")} request for the target id {d.Item2} ({d.Item1})");
				var sKey = d.Item1;
				var dataId = d.Item2;
				if (_proxies.ContainsKey(sKey))
				{
					if (remove)
						_proxies[sKey].RemoveSub.Invoke(src, dataId);
					else
						_proxies[sKey].AddSub.Invoke(src, dataId);
				}
			}
		}

		long lastEmittedMsgId;
		Vector3D? _gsPrevPos;
		int _gsPrevPosStamp;

		HashSet<long> uniq_set = new HashSet<long>();

		List<TargetLite> processedTargets = new List<TargetLite>();

		List<ITgpTargeter> Targeters;

		public interface ITgpTargeter
		{
			//bool HasTarget { get; }
			//void Update(int tick);
			//List<TargetLite> Results { get; }
			string GetTypeName { get; }

		}

		void HandleGridSense()
		{
			uniq_set.Clear();
			processedTargets.Clear();

			if (Toggle.C.Check("pirate-scan") && (MainCtrl as IMyRemoteControl != null))
			{
				Vector3D pPos = Vector3D.Zero;
				if (((IMyRemoteControl)MainCtrl).GetNearestPlayer(out pPos))
				{
					var result = new TargetLite
					{
						Id = 100,
						Name = "sprt-gnp"
					};

					if (_gsPrevPos.HasValue)
						result.Velocity = (pPos - _gsPrevPos.Value) / ((E.T - _gsPrevPosStamp) / 60f); // normalize time
					else
						result.Velocity = Vector3D.Zero;

					_gsPrevPos = pPos;
					_gsPrevPosStamp = E.T;

					result.Position = pPos;

					processedTargets.Add(result);
				}
				else
					_gsPrevPos = null;
			}


			TTPool.ToRemove.Clear();
			foreach (var t in TTPool.Targets.Values)
			{
				if (TGatingComp.ConsiderBanning(t) || (E.T - t.TickStamp > 300))
					t.MarkedForRemoval = true;
				if (t.MarkedForRemoval)
				{
					E.DebugLog($"Target {t.Info.EntityId} is being removed");
					TTM.OnTargetRootRemoved(t);
					TTPool.ToRemove.Add(t.Info.EntityId);
				}
				else
				{
					t.Integrate();
					t.Approved = true;
					TTM.OnTargetRootUpdated(t);
				}
			}
			foreach (var id in TTPool.ToRemove)
			{
				TTPool.Targets.Remove(id);
			}

			targeterTurret.Update(E.T, TGatingComp); // OnTargetEncountered invocation for new targets

			targeterWc?.Update(E.T, TGatingComp);
			
			targeterRadar?.Update(E.T, TGatingComp);

			TTPool.ToAdd.Clear();
			foreach (var t in TTPool.Targets.Values)
			{
				if (t.TickStamp != E.T)
					targeterRC.Update(t);
				if (E.T - t.TickStamp > 20)
				{
					// rc recovery
					var p = t.GetRecastPosition();
					var cam = RCaster.GetCamForPos(p);
					if (cam != null)
					{
						var res = RCaster.CastBurst(p, cam, (float)(p - cam.GetPosition()).Length(), (float)t.Info.BoundingBox.HalfExtents.Length(), t.Info.EntityId);
						if (res.Count == 0)
							t.MarkedForRemoval = true;
						else
							TTPool.ToAdd.AddRange(res);
						metrics.ScanBurstsCount++;
					}
				}
			}

			foreach (var cast in TTPool.ToAdd)
			{
				TTPool.AddTrackedTarget(cast, targeterRC.GetTypeName);
			}

			foreach (var t in Tsc.GetTargets(TTPool.Targets, Tom, GlobalSelector))
			{
				if (uniq_set.Add(t.Id)) // not needed?
					processedTargets.Add(t);
			}

			STPool.PopulateTargets(processedTargets);
		}



		TrackedTargetsPool TTPool;
		public class TrackedTargetsPool
		{
			public Dictionary<long, TrackedTarget> Targets = new Dictionary<long, TrackedTarget>();
			public List<MyDetectedEntityInfo> ToAdd = new List<MyDetectedEntityInfo>();
			public HashSet<long> ToRemove = new HashSet<long>();
			TargetGatingComponent _tgc;

			public TrackedTargetsPool(TargetGatingComponent tgc)
			{
				_tgc = tgc;
			}

			public TrackedTarget GetById(long id)
			{
				TrackedTarget val;
				Targets.TryGetValue(id, out val);
				return val;
			}

			public TrackedTarget AddTrackedTarget(MyDetectedEntityInfo mdei, string source)
			{
				var tt = GetById(mdei.EntityId);
				if (tt == null)
				{
					if (!_tgc.ConsiderAddTarget(ref mdei))
						return null;

					// if the target Position is in empty space later then fallback to hit position
					// 0.9f for burying a bit from HP into BB center
					var baseOffset = Vector3D.Rotate((mdei.HitPosition.Value - mdei.Position) * 0.9f, MatrixD.Transpose(mdei.Orientation));
					E.DebugLog($"Added target {mdei.EntityId}");
					tt = new TrackedTarget(mdei, E.T, baseOffset, source);
					Targets.Add(mdei.EntityId, tt);
					// reset turret offset?
				}
				else
					tt.Actualize(mdei, E.T);

				return tt;
			}
		}

		public class TrackedTarget
		{
			public MyDetectedEntityInfo Info { get; private set; }
			public Vector3D PredictedPosition;
			Vector3D _baseOffset;
			public bool UsingOffset;
			public int TickStamp;
			public bool MarkedForRemoval;
			public bool Approved;
			public int SelectedOffsetIndex;
			public float? ThreatScore;
			public string Source { get; private set; }

			public TrackedTarget(MyDetectedEntityInfo info, int tick, Vector3D baseOffset, string source)
			{
				_baseOffset = baseOffset;
				Source = source;
				Actualize(info, tick);
			}
			public void Actualize(MyDetectedEntityInfo info, int tick)
			{
				Info = info;
				PredictedPosition = info.Position;
				TickStamp = tick;
			}
			public Vector3D GetRecastPosition()
			{
				if (UsingOffset)
					return PredictedPosition + Vector3D.Rotate(_baseOffset, Info.Orientation);
				else
					return PredictedPosition;
			}
			public void Integrate()
			{
				PredictedPosition += Info.Velocity / 60f;
			}

			Vector3D _wbPrevVel;
			int _wbCtr;
			public bool IsInertial(ref Vector3D nG)
			{
				if (Info.Velocity != Vector3D.Zero)
				{
					var acc = (Info.Velocity - _wbPrevVel) * 60;
					if ((acc == Vector3D.Zero) || (nG == acc))
					{
						_wbCtr++;
					}
					if (_wbCtr > 10)
					{
						E.DebugLog($"{Source}/{Info.EntityId}: inert filter fails");
						return true;
					}
				}
				else
				{
					_wbCtr = 0;
				}
				_wbPrevVel = Info.Velocity;
				return false;
			}
		}

		StaticTargetsPool STPool = new StaticTargetsPool();
		public class StaticTargetsPool
		{
			List<StaticTarget> Targets = new List<StaticTarget>();
			long _idCtr = 9000;
			public int Count => Targets.Count;
			public void StaticCast(Raycaster caster, IMyShipController ctrl, int expiration)
			{
				Vector3D pt;
				if (caster.GetFwCastPosition(null, ctrl, out pt, 500))
				{
					var cam = caster.GetCamForPos(pt);
					if (cam != null)
					{
						var hit = cam.Raycast(pt);
						if (hit.HitPosition.HasValue)
						{
							Targets.Add(new StaticTarget { Mdei = hit, Name = "stp", Pos = hit.HitPosition.Value, Id = _idCtr++, ExpiresIn = expiration });
						}
					}
				}
			}

			public void PopulateTargets(List<TargetLite> output)
			{
				foreach (var t in Targets)
				{
					output.Add(new TargetLite { Mdei = t.Mdei, Name = t.Name, Position = t.Pos, Id = t.Id });
				}
			}

			public void UpdateAfter(int tick)
			{
				foreach (var t in Targets)
				{
					t.Integrate(tick);
				}

				Targets.RemoveAll(x => x.MarkedForRemoval);
			}

			int _col = -1;
			int _row = 0;
			public void Sweep(IMyShipController ctrl, Raycaster caster, IMyGridProgramRuntimeInfo info, TrackedTargetsPool ttPool)
			{
				if (Toggle.C.Check("sweep"))
				{
					_col = 0;
					Toggle.C.Set("sweep", false);
				}
				if (_col != -1)
				{
					var wm = ctrl.WorldMatrix;
					var bpos = wm.Translation + wm.Forward * 3000;
					var x = wm.Left * (100 - _col) * 7;

					while (info.CurrentInstructionCount < 40000)
					{
						var y = wm.Up * (_row - 50) * 7;
						var pos = bpos + x + y;
						// TODO: store last found index to cope with large cam numbers
						var cam = caster.GetCamForPos(pos);
						if (cam != null)
						{
							E.Draw(pos, "Circle", "ff00aa", 2, null);

							var hit = cam.Raycast(pos);
							if (hit.HitPosition.HasValue)
							{
								Targets.Add(new StaticTarget { Mdei = hit, Name = "stp", Pos = hit.HitPosition.Value, Id = _idCtr++ });
								//ttPool.AddTrackedTarget(hit, "sweep");
							}
						}
						_row++;
					}
					
					if (_row > 100)
					{
						_row = 0;
						_col++;
					}
					if (_col > 199)
						_col = -1;
				}
			}

			class StaticTarget
			{
				public long Id;
				public Vector3D Pos;
				public MyDetectedEntityInfo Mdei;
				public string Name;
				public int ExpiresIn = -1;
				public bool MarkedForRemoval;
				public void Integrate(int tick)
				{
					if (ExpiresIn != -1)
					{
						ExpiresIn--;
						if (ExpiresIn == 0)
							MarkedForRemoval = true;
					}

					Pos += Mdei.Velocity / 60f;
				}
			}
		}


		TargetSelectorComponent Tsc;
		MultiTrackingSelector GlobalSelector = MultiTrackingSelector.Everything;

		public class TargetSelectorComponent
		{
			int SelectedTargetIndex;
			public TrackedTarget SelectedTrackedTarget;
			List<TrackedTarget> _validTargets = new List<TrackedTarget>();
			List<TargetLite> _results = new List<TargetLite>();
			TargetOffsetRepo _tofRepo;
			public TargetSelectorComponent(TargetOffsetRepo tofRepo)
			{
				_tofRepo = tofRepo;
			}
			public List<TargetLite> GetTargets(Dictionary<long, TrackedTarget> _trackedTargets, TargetOffsetManager _tom, MultiTrackingSelector selector)
			{
				SelectedTrackedTarget = null;
				_results.Clear();
				_validTargets.Clear();
				//E.Echo($"TSC si: {SelectedTargetIndex}");

				int i = 0;
				foreach (var tt in _trackedTargets.Values)
				{
					if (tt.Approved)
						_validTargets.Add(tt);
					if (SelectedTargetIndex == i)
					{
						if (!tt.MarkedForRemoval)
							SelectedTrackedTarget = tt;
						else
							SelectedTargetIndex = 0;
					}
					i++;
				}

				switch (selector)
				{
					case MultiTrackingSelector.Everything:
						foreach (var tt in _validTargets)
						{
							int offset_Id_suffix = 0;
							foreach (var off in _tom.GetWorldOffsets(tt.Info.EntityId,
									tt.Info.Orientation, tt.PredictedPosition))
							{
								offset_Id_suffix++;
								var tl = new TargetLite
								{
									Mdei = tt.Info,
									Id = tt.Info.EntityId + offset_Id_suffix,
									Name = offset_Id_suffix == 1 ? "tgp" : null,
									Position = off,
									Velocity = tt.Info.Velocity
								};

								if (offset_Id_suffix == 1)
									_tofRepo.AddMeta(ref tl, tt.Info.EntityId);
								_results.Add(tl);
							}
						}
						break;
					case MultiTrackingSelector.AllTargets:
						foreach (var tt in _validTargets)
						{
							var tl = new TargetLite
							{
								Mdei = tt.Info,
								Id = tt.Info.EntityId,
								Name = "tgp",
								Position = tt.Info.Position,
								Velocity = tt.Info.Velocity
							};
							_tofRepo.AddMeta(ref tl, tt.Info.EntityId);
							_results.Add(tl);
						}
						break;
					case MultiTrackingSelector.Selected:
						if (_validTargets.Count > 0)
						{
							var offIndex = Math.Min(_validTargets.Count - 1, SelectedTargetIndex);
							var t = _validTargets.ElementAt(offIndex);
							var tl = new TargetLite
							{
								Mdei = t.Info,
								Id = t.Info.EntityId,
								Name = "tgp",
								Position = t.Info.Position,
								Velocity = t.Info.Velocity
							};

							tl.Position = _tom.GetOffset(tl.Id, t.SelectedOffsetIndex, t.Info.Orientation, t.PredictedPosition);

							_tofRepo.AddMeta(ref tl, t.Info.EntityId);
							_results.Add(tl);
						}
						break;
					case MultiTrackingSelector.AllOffsets:
						if (_validTargets.Count > 0)
						{
							var offIndex = Math.Min(_validTargets.Count - 1, SelectedTargetIndex);
							var t = _validTargets.ElementAt(offIndex);

							var tBase = new TargetLite
							{
								Mdei = t.Info,
								Id = t.Info.EntityId,
								Name = "tgp",
								Position = t.GetRecastPosition(),
								Velocity = t.Info.Velocity
							};
							_tofRepo.AddMeta(ref tBase, t.Info.EntityId);
							_results.Add(tBase);

							int offset_Id_suffix = 0;
							foreach (var off in _tom.GetWorldOffsets(t.Info.EntityId,
									t.Info.Orientation, t.PredictedPosition))
							{
								offset_Id_suffix++;
								var tl = new TargetLite
								{
									Mdei = t.Info,
									Id = t.Info.EntityId + offset_Id_suffix,
									Position = off,
									Velocity = t.Info.Velocity
								};
								_results.Add(tl);
							}
						}
						break;
				}

				return _results;
			}

			public void SetIndex(int i)
			{
				SelectedTargetIndex = i;
			}

			public void CycleTarget(Dictionary<long, TrackedTarget> _trackedTargets, bool reverse)
			{
				var count = _trackedTargets.Values.Count(x => !x.MarkedForRemoval);

				for (int i = 0; i < count; i++)
				{
					if (i == SelectedTargetIndex)
					{
						if (reverse)
						{
							var newInd = i - 1;
							if (newInd < 0)
								newInd = count - 1;
							SelectedTargetIndex = newInd;
						}
						else
						{
							var newInd = i + 1;
							if (newInd >= count)
								newInd = 0;
							SelectedTargetIndex = newInd;
						}
						return;
					}
				}
				SelectedTargetIndex = 0;
			}
		}

		Raycaster RCaster;
		public class Raycaster
		{
			List<IMyCameraBlock> _cams;
			IMyCameraBlock CoaxialCam;
			public double Resource { get; private set; }
			double _resourcePrev;
			public double ResourceDiff { get; private set; }

			public int CamsCount => _cams.Count;

			//float step = 5f;
			int max_gen = 20;
			int rc_round_count = 1;

			public Raycaster(List<IMyCameraBlock> cams)
			{
				// cam gets charge at 2000 m/s rate by default
				_cams = cams;
				_cams.ForEach(x => x.EnableRaycast = true);
				CoaxialCam = _cams.FirstOrDefault(x => x.Name.Contains("coaxial-cam"));
				// total needed for scan burst
				for (int g = 1; g < max_gen; g++)
				{
					rc_round_count += g * 6;
				}
			}

			public IMyCameraBlock GetActiveCam()
			{
				for (int i = 0; i < _cams.Count; i++)
					if (_cams[i].IsActive)
						return _cams[i];
				return null;
			}

			public IMyCameraBlock GetCamForPos(Vector3D pos)
			{
				var ac = GetActiveCam();
				if (ac?.CanScan(pos) == true)
					return ac;
				for (int i = 0; i < _cams.Count; i++)
					if (_cams[i].CanScan(pos))
						return _cams[i];
				return null;
			}

			public bool GetFwCastPosition(IMyEntity fwRef, IMyShipController elevationGetter, out Vector3D res, float surfaceAdjustment = 0)
			{
				fwRef = fwRef ?? GetActiveCam();
				if (fwRef == null)
				{
					res = default(Vector3D);
					return false;
				}

				var d = Variables.Get<float>("raycast-range");
				res = fwRef.GetPosition() + fwRef.WorldMatrix.Forward * d;

				if (surfaceAdjustment != 0)
				{
					double altitude;
					Vector3D pPos;
					if (elevationGetter.TryGetPlanetElevation(MyPlanetElevation.Surface, out altitude))
					{
						elevationGetter.TryGetPlanetPosition(out pPos);
						var plToMeDistance = (elevationGetter.GetPosition() - pPos).Length();
						// the idea is to clamp by sphere around planet, 500m above. Negative for hitting grids, positive for voxels
						altitude += surfaceAdjustment;
						var bs = new BoundingSphereD(pPos, plToMeDistance - altitude);
						var inters = bs.Intersects(new RayD(fwRef.GetPosition(), fwRef.WorldMatrix.Forward));
						if (inters.HasValue && (inters > 0) && inters < d) // zero means bs contains our ray origin
						{
							E.DebugLog($"Cast distance adjusted from {d:f2} to {inters.Value:f2} due to planet proximity");
							d = (float)inters.Value;
						}
						res = fwRef.GetPosition() + fwRef.WorldMatrix.Forward * d;
					}
				}

				return true;
			}

			List<MyDetectedEntityInfo> _rcBurstBuffer = new List<MyDetectedEntityInfo>();
			public List<MyDetectedEntityInfo> CastBurst(Vector3D refPos, IMyCameraBlock refCaster, float depth, float rsl, long searchForId = 0)
			{
				_rcBurstBuffer.Clear();

				//int gen = 1;
				int gen = 0;
				int i = 0;
				bool done = false;
				foreach (var cam in _cams)
				{
					if (done)
					{
						break;
					}

					while (cam.CanScan(depth))
					{
						double angle = 60f / 180f * Math.PI * i / (gen == 0 ? 1 : gen);
						var radInterval = rsl * 0.866f; // 2 cos(30) * R
						var xy = new Vector2((float)Math.Cos(angle), (float)Math.Sin(angle)) * radInterval * gen;
						var p = refPos + refCaster.WorldMatrix.Up * xy.X + refCaster.WorldMatrix.Left * xy.Y;
						var r = cam.Raycast(p);

						// TODO: cmdr, decaying msg/entity
						//if (cam.CanScan(p))
						//	E.Draw(p, "Circle", "ff0000", 1, null);
						//else
						//	break;

						if (r.HitPosition.HasValue)
						{
							if (!ValidateRcNonVoxel(r.Type)) // interrupt for performance
							{
								done = true;
								break;
							}
							if (ValidateRelations(r.Relationship))
								_rcBurstBuffer.Add(r);
							if (r.EntityId == searchForId)
							{
								done = true;
								break;
							}
						}

						if (i++ > 6 * gen)
						{
							i = 0;
							gen++;
							if (gen > max_gen)
							{
								done = true;
								break;
							}
						}
					}
				}

				E.DebugLog($"CastBurst, searchForId {searchForId}, count: {_rcBurstBuffer.Count}");
				return _rcBurstBuffer;
			}

			public bool TryActualize(long id, Vector3D pos, out MyDetectedEntityInfo mdei)
			{
				var cam = _cams.OrderByDescending(x => x.AvailableScanRange).FirstOrDefault(x => x.CanScan(pos)); // TODO: remove alloc, cache results
				if (cam != null)
				{
					var r = cam.Raycast(pos + Vector3D.Normalize(pos - cam.GetPosition()) * 20f);
					//E.DebugLog($"act {cam.CustomName}, emplty: {r.IsEmpty()}, ppos: GPS:rcT:{VectorOpsHelper.V3DtoBroadcastString(ppos)}:");
					if (r.HitPosition.HasValue && r.EntityId == id)
					{
						//E.DebugLog($"rPos: GPS:rcT:{VectorOpsHelper.V3DtoBroadcastString(r.HitPosition.Value)}:");
						mdei = r;
						return true;
					}
				}
				mdei = new MyDetectedEntityInfo();
				return false;
			}

			public void UpdateAfter(int tick)
			{
				Resource = 0;
				foreach (var c in _cams)
					Resource += c.AvailableScanRange;
				ResourceDiff = Resource - _resourcePrev;
				_resourcePrev = Resource;
			}

			public bool CastTriangle(out MyTuple<Vector3D, Vector3D, Vector3D, Vector3D> res, float upDelta, float leftDelta, float range)
			{
				var activeCam = GetActiveCam() ?? CoaxialCam;
				if (activeCam != null)
				{
					var info = Cast(activeCam, activeCam.GetPosition() + activeCam.WorldMatrix.Forward * range);
					if (info.HasValue)
					{
						var dei = info.Value;
						if (!dei.IsEmpty())
						{
							var castedSurfacePoint = dei.HitPosition.Value;
							var p1 = castedSurfacePoint + activeCam.WorldMatrix.Up * upDelta + activeCam.WorldMatrix.Forward * 100;
							var p2 = castedSurfacePoint + activeCam.WorldMatrix.Left * leftDelta + activeCam.WorldMatrix.Forward * 100;

							if (activeCam.CanScan(p1))
							{
								var cast1 = activeCam.Raycast(p1);
								if (!cast1.IsEmpty())
								{
									if (activeCam.CanScan(p2))
									{
										var cast2 = activeCam.Raycast(p2);
										if (!cast2.IsEmpty())
										{
											var castedNormal = -Vector3D.Normalize(Vector3D.Cross(cast2.HitPosition.Value - castedSurfacePoint, cast1.HitPosition.Value - castedSurfacePoint));
											res = new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>(castedSurfacePoint, cast1.HitPosition.Value, cast2.HitPosition.Value, castedNormal);
											return true;
										}
									}
								}
							}
						}
					}
				}

				res = new MyTuple<Vector3D, Vector3D, Vector3D, Vector3D>();
				return false;
			}

			MyDetectedEntityInfo? Cast(IMyCameraBlock cam, Vector3D position)
			{
				if (cam.CanScan(position))
				{
					var info = cam.Raycast(position);
					if (!info.IsEmpty())
						return info;
				}
				return null;
			}

			public void ProbeSurfaceHandler(string[] cmdString, Action<Vector3D, Vector3D> onSuccess)
			{
				var range = cmdString.Length > 2 ? float.Parse(cmdString[2]) : Variables.Get<float>("raycast-range");

				var activeCam = GetActiveCam() ?? CoaxialCam;
				if ((activeCam?.AvailableScanRange > range) == false)
				{
					E.DebugLog($"No active camera with sufficient range of {range}");
					return;
				}
				MyTuple<Vector3D, Vector3D, Vector3D, Vector3D> tri;
				if (CastTriangle(out tri, 10, 20, range))
				{
					var castedNormal = tri.Item4;

					var toPoint = tri.Item1 - activeCam.WorldMatrix.Translation;

					onSuccess.Invoke(tri.Item1, Vector3D.Normalize(castedNormal));
				}
			}

			public static bool ValidateRcNonVoxel(MyDetectedEntityType type)
			{
				return type != MyDetectedEntityType.Planet && type != MyDetectedEntityType.Asteroid;
			}

			public static bool ValidateRelations(MyRelationsBetweenPlayerAndBlock rel)
			{
				return Toggle.C.Check("lock-friendlies")
					|| rel == MyRelationsBetweenPlayerAndBlock.Enemies
					|| rel == MyRelationsBetweenPlayerAndBlock.Neutral
					|| rel == MyRelationsBetweenPlayerAndBlock.NoOwnership;
			}
		}


		RaycastTrackerTargeter targeterRC;
		public class RaycastTrackerTargeter : ITgpTargeter
		{
			TargetSelectorComponent _tsc;

			TrackedTargetsPool _pool;
			public string GetTypeName => "RaycastTrackerTargeter";

			TargetOffsetManager _tom;

			public Action<IMyCameraBlock, long, Vector3D> OnCamRcSuccess;
			//public Action<TrackedTarget> OnTargetRootUpdated;
			//public Action<TrackedTarget> OnTargetRootRemoved;

			IMyShipController _ctrl;

			Raycaster _caster;

			public RaycastTrackerTargeter(Raycaster caster, IMyShipController ctrl, TargetOffsetManager tom, TargetSelectorComponent tsc, 
					TrackedTargetsPool pool)
			{
				_caster = caster;

				_ctrl = ctrl;
				_tom = tom;

				_tsc = tsc;
				_pool = pool;
			}

			public void CheckEncounter(long id, Vector3D p)
			{
				var tt = _pool.GetById(id);
				if (tt == null)
				{
					var cam = _caster.GetCamForPos(p);
					if (cam != null)
					{
						var baseCast = cam.Raycast(p);
						if (baseCast.HitPosition.HasValue && (baseCast.EntityId == id))
						{
							OnCamRcSuccess?.Invoke(null, baseCast.EntityId, p);
							_pool.AddTrackedTarget(baseCast, $"{this.GetTypeName}(EncounterLock)");
						}
					}

					//var hitId = _caster.Cast(p, _pool, $"{this.GetTypeName}(EncounterLock)");
					//if (hitId.HasValue && hitId.Value == id)
					//{
					//	OnCamRcSuccess?.Invoke(null, id, p);
					//}
				}
			}

			public void Update(TrackedTarget tt)
			{
				var ppos = tt.GetRecastPosition();
				MyDetectedEntityInfo mdei;
				if (_caster.TryActualize(tt.Info.EntityId, ppos, out mdei))
				{
					tt.Actualize(mdei, E.T);
				}
				else
				{
					tt.UsingOffset = true; // failed Position cast, next time try first offset
				}
			}

			public void ForwardCast(IMyEntity fwRef, float resolution)
			{
				Vector3D pt;
				if (_caster.GetFwCastPosition(fwRef, _ctrl, out pt, -500))
				{
					var cam = _caster.GetCamForPos(pt);
					if (cam != null)
					{
						//var hit = cam.Raycast(pt);

						TrackedTarget added = null;
						var baseCast = cam.Raycast(pt);
						if (baseCast.HitPosition.HasValue && (baseCast.EntityId != cam.EntityId))
						{
							if (!Raycaster.ValidateRcNonVoxel(baseCast.Type) || !Raycaster.ValidateRelations(baseCast.Relationship))
							{
								E.DebugLog("Cast was successful but target type/relations check has failed");
								return;
							}
							added = _pool.AddTrackedTarget(baseCast, this.GetTypeName);
							var offsetIndex = _tom.ConsiderOffset(baseCast.EntityId, baseCast.Orientation, baseCast.Position, baseCast.HitPosition.Value, false);
							if (offsetIndex > 0)
								added.SelectedOffsetIndex = offsetIndex;
						}
						else if (Toggle.C.Check("raycast-burst"))
						{
							var depth = (float)(pt - cam.GetPosition()).Length() + 50;
							var res = _caster.CastBurst(pt, cam, resolution, depth);
							if (res.Count > 0)
							{
								foreach (var cast in res)
								{
									if (Raycaster.ValidateRcNonVoxel(cast.Type) && Raycaster.ValidateRelations(cast.Relationship))
									{
										added = _pool.AddTrackedTarget(cast, this.GetTypeName);
										_tom.ConsiderOffset(cast.EntityId, cast.Orientation, cast.Position, cast.HitPosition.Value, false);
									}
								}
							}
						}

						if (added != null)
						{
							int i = 0;
							foreach (var tt in _pool.Targets.Values)
							{
								if (tt == added)
								{
									_tsc.SetIndex(i);
								}
								i++;
							}
							OnCamRcSuccess?.Invoke(cam, added.Info.EntityId, pt);
							_tom.SetRaycastSource();
						}
					}
				}
			}
		}

		TurretAiTargeter targeterTurret;
		public class TurretAiTargeter : ITgpTargeter
		{
			TrackedTargetsPool _pool;

			HashSet<DesignatorProxy> _designators = new HashSet<DesignatorProxy>();

			public int Count => _designators.Count;

			public string GetTypeName => "TurretAiTargeter";

			public Action<long, Vector3D> OnTargetEncountered;

			MyDetectedEntityInfo _mdei;

			TargetOffsetManager _tom;

			public long? ScanModeMinSize;
			public long? ScanModeMinD;
			public string ScanModeTurretTag;

			public TurretAiTargeter(TargetOffsetManager tom, TrackedTargetsPool pool)
			{
				_tom = tom;
				_pool = pool;
			}

			public void AddDesignators(List<IMyLargeTurretBase> turrets, List<IMyTurretControlBlock> controllers)
			{
				foreach (var c in turrets)
				{
					_designators.Add(new DesignatorProxy(c, _tom, this));
					c.Range = 5000;
				}
				foreach (var c in controllers)
				{
					_designators.Add(new DesignatorProxy(c, _tom, this));
					c.Range = 5000;
				}
			}

			public void Update(int tick, TargetGatingComponent tgc)
			{
				foreach (var d in _designators)
				{
					if (d.Update(ref _mdei))
					{
						var tt = _pool.GetById(_mdei.EntityId);
						if (tt == null)
						{
							if (!tgc.ConsiderAddTarget(ref _mdei))
							{
								d.Reset();
								continue;
							}
							//VRageRender.MyRenderProxy.DebugDrawText3D(_mdei.HitPosition.Value, $"x", Color.Red, 1f, false, persistent: true);
							tt = new TrackedTarget(_mdei, E.T, Vector3D.Zero, this.GetTypeName);
							_pool.Targets.Add(_mdei.EntityId, tt);
							OnTargetEncountered?.Invoke(_mdei.EntityId, _mdei.HitPosition.Value);
						}
						else
							tt.Actualize(_mdei, E.T);

						// not needed for turret targeter
						//_tsc.SetIndex(_trackedTargets.IndexOf(tt));
					}
				}
			}

			public void StopScan(long id)
			{
				foreach (var t in _designators)
				{
					t.StopScan();
				}
			}

			class DesignatorProxy
			{
				IMyLargeTurretBase _d;
				IMyTurretControlBlock _c;
				TargetOffsetManager _tom;
				bool _longRange;

				int _offsetsFoundUnique;
				int _offsetsFoundTotal;

				int _scanModeDullLimit = 300;
				int _scanModeLastAdditionStamp;
				long _scanModeLastId;
				bool _scanMode = true;

				bool _currentScanComplete;

				TurretAiTargeter _container;

				public DesignatorProxy(IMyTurretControlBlock c, TargetOffsetManager tom, TurretAiTargeter container)
				{
					_c = c;
					_tom = tom;
					_container = container;
				}

				public DesignatorProxy(IMyLargeTurretBase t, TargetOffsetManager tom, TurretAiTargeter container)
				{
					_d = t;
					_tom = tom;
					_container = container;
				}

				public bool Update(ref MyDetectedEntityInfo mdei)
				{
					bool res = false;
					var i =  _d?.GetTargetedEntity() ?? _c.GetTargetedEntity();
					if (!i.IsEmpty())
					{
						mdei = i;
						//VRageRender.MyRenderProxy.DebugDrawText3D(mdei.HitPosition.Value, $"x", Color.Red, 1f, false, persistent: true);
						res = true;
						if (_scanModeLastId != mdei.EntityId)
						{
							_offsetsFoundUnique = 0;
							_offsetsFoundTotal = 0;
							_scanModeLastAdditionStamp = 0;
							_currentScanComplete = false;

							_scanMode = string.IsNullOrEmpty(_container.ScanModeTurretTag) || _d == null || _d.CustomName.Contains(_container.ScanModeTurretTag);
							_scanMode &= !_container.ScanModeMinD.HasValue || _container.ScanModeMinD.Value < Vector3D.Distance(mdei.Position, _d?.GetPosition() ?? _c.GetPosition());
							_scanMode &= !_container.ScanModeMinSize.HasValue || _container.ScanModeMinSize.Value < mdei.BoundingBox.Extents.Length();

							E.DebugLog($"{_d?.CustomName ?? _c.CustomName} considering scan for {mdei.EntityId}: {_scanMode}");
						}
						_scanModeLastId = mdei.EntityId;

						if (_scanMode)
						{
							_offsetsFoundTotal++;
							if (_tom.ConsiderOffset(mdei.EntityId, mdei.Orientation, mdei.Position, i.HitPosition.Value, true) > 0)
							{
								_scanModeLastAdditionStamp = E.T;
								_offsetsFoundUnique++;
							}

							Reset();
						}
					}

					_scanMode &= !_currentScanComplete && ((_scanModeLastAdditionStamp == 0) || (E.T - _scanModeLastAdditionStamp < _scanModeDullLimit));

					if (_scanMode)
						E.Echo($"{_d?.CustomName ?? _c.CustomName} scanning");

					if (_offsetsFoundTotal > 0)
					{
						E.Echo($"polls: {_offsetsFoundUnique}/{_offsetsFoundTotal}, lim: {E.T - _scanModeLastAdditionStamp}");
						E.Echo($"rate: {(float)_offsetsFoundUnique / _offsetsFoundTotal:f2}");
					}

					return res;
				}

				public void Reset()
				{
					if (_d != null)
						_d.ResetTargetingToDefault();
					//else
					//_c.Range--;
				}

				public void StopScan()
				{
					_scanMode = false;
					_currentScanComplete = true;
				}

				public override int GetHashCode()
				{
					if (_c != null)
						return _c.GetHashCode();
					return _d.GetHashCode();
				}
			}
		}

		WcTargeter targeterWc;
		public class WcTargeter : ITgpTargeter
		{
			public string GetTypeName => "WcTargeter";
			TrackedTargetsPool _pool;
			TargetOffsetManager _tom;
			Action<IMyTerminalBlock, IDictionary<long, MyDetectedEntityInfo>> _getSortedThreats;
			IMyProgrammableBlock _me;

			public WcTargeter(TrackedTargetsPool pool, TargetOffsetManager tom, Action<IMyTerminalBlock, IDictionary<long, MyDetectedEntityInfo>> provider, IMyProgrammableBlock me)
			{
				_pool = pool;
				_getSortedThreats = provider;
				_me = me;
				_tom = tom;
			}

			Dictionary<long, MyDetectedEntityInfo> _buffer = new Dictionary<long, MyDetectedEntityInfo>();
			//Dictionary<MyDetectedEntityInfo, float> _fakeTargetsProfiling;
			public void Update(int tick, TargetGatingComponent tgc)
			{
				_buffer.Clear();

				/*if (_fakeTargetsProfiling == null)
				{
					_fakeTargetsProfiling = new Dictionary<MyDetectedEntityInfo, float>();
					var r = new Random();
					for (int i = 0; i < 31; i++)
					{
						var pos = _me.GetPosition() + new Vector3(1000 + r.NextDouble() * 500,
							1000 + r.NextDouble() * 500, 1000 + r.NextDouble() * 500);
						var bb = BoundingBoxD.CreateFromSphere(new BoundingSphereD(pos, 20));
						var data = new MyDetectedEntityInfo((long)r.Next(), "x", MyDetectedEntityType.LargeGrid, pos,
							MatrixD.Identity, Vector3.Zero,
							MyRelationsBetweenPlayerAndBlock.Enemies, bb, E.T);
						_fakeTargetsProfiling.Add(data, 10);
					}
				}
				foreach (var f in _fakeTargetsProfiling)
				{
					_buffer.Add(f.Key, f.Value);
				}*/

				try
				{
					_getSortedThreats(_me, _buffer);
				}
				catch (Exception e)
				{
					E.Fail(e.ToString());
				}
				
				foreach (var t in _buffer)
				{
					var mdei = t.Value;
					if (mdei.EntityId == 0 || !ValidateRelations(mdei.Relationship))
						continue;
					var tt = _pool.GetById(mdei.EntityId);
					if (tt == null)
					{
						if (!tgc.ConsiderAddTarget(ref mdei))
						{
							continue;
						}

						_tom.ConsiderOffset(mdei.EntityId, mdei.Orientation, mdei.Position, mdei.HitPosition ?? mdei.Position, true);
						//VRageRender.MyRenderProxy.DebugDrawText3D(_mdei.HitPosition.Value, $"x", Color.Red, 1f, false, persistent: true);
						tt = new TrackedTarget(mdei, E.T, Vector3D.Zero, this.GetTypeName);
						//tt.ThreatScore = t.Value;
						tt.ThreatScore = 10;
						_pool.Targets.Add(mdei.EntityId, tt);
						//OnTargetEncountered?.Invoke(_mdei.EntityId, _mdei.HitPosition.Value);
					}
					else
					{
						//tt.ThreatScore = t.Value;
						tt.ThreatScore = 10;
						tt.Actualize(mdei, E.T);
					}
				}
			}

			HashSet<int> allowed = new HashSet<int> { (int)MyRelationsBetweenPlayerAndBlock.Enemies };
			public void AllowWcRelation(string rel)
			{
				MyRelationsBetweenPlayerAndBlock val;
				if (Enum.TryParse(rel, out val))
					allowed.Add((int)val);
			}

			bool ValidateRelations(MyRelationsBetweenPlayerAndBlock rel)
			{
				return allowed.Contains((int)rel);
			}
		}
    RadarTargeter targeterRadar;
		public class RadarTargeter : ITgpTargeter{
			public string GetTypeName => "RadarTargeter";
			TrackedTargetsPool _pool;
			TargetOffsetManager _tom;
			Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>> _getDetectedRadarTargets;
			IMyProgrammableBlock _me;

			public RadarTargeter(TrackedTargetsPool pool, TargetOffsetManager tom, Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>> provider, IMyProgrammableBlock me)
			{
				_pool = pool;
				_getDetectedRadarTargets = provider;
				_me = me;
				_tom = tom;
			}

			List<MyDetectedEntityInfo> _buffer = new List<MyDetectedEntityInfo>();
			public void Update(int tick, TargetGatingComponent tgc)
			{
				_buffer.Clear();
				try
				{
					_buffer = _getDetectedRadarTargets(_me);
				}
				catch (Exception e)
				{
					E.Fail(e.ToString());
				}
				
				foreach (var t in _buffer)
				{
					var mdei = t;
					if (mdei.EntityId == 0 || !ValidateRelations(mdei.Relationship))
						continue;
					var tt = _pool.GetById(mdei.EntityId);
					if (tt == null)
					{
						if (!tgc.ConsiderAddTarget(ref mdei))
						{
							continue;
						}

						_tom.ConsiderOffset(mdei.EntityId, mdei.Orientation, mdei.Position, mdei.HitPosition ?? mdei.Position, true);
						tt = new TrackedTarget(mdei, E.T, Vector3D.Zero, this.GetTypeName);
						_pool.Targets.Add(mdei.EntityId, tt);
					}
					else
					{
						tt.ThreatScore = 10;
						tt.Actualize(mdei, E.T);
					}
				}
			}

			HashSet<int> allowed = new HashSet<int> { (int)MyRelationsBetweenPlayerAndBlock.Enemies };
			public void AllowRadarRelation(string rel)
			{
				MyRelationsBetweenPlayerAndBlock val;
				if (Enum.TryParse(rel, out val))
					allowed.Add((int)val);
			}

			bool ValidateRelations(MyRelationsBetweenPlayerAndBlock rel)
			{
				return allowed.Contains((int)rel);
			}
		}

		TargetGatingComponent TGatingComp;
		public class TargetGatingComponent
		{
			HashSet<long> _bannedPermanent = new HashSet<long>();
			HashSet<long> _bannedShort = new HashSet<long>();
			IMyShipController _rc;
			Vector3D _nG;

			public bool DenyInertial;
			public float DenySmallerThan;
			public float DenyMissingSphere;
			public float DenyWcThreatLowerThan;

			int _tickStampBanCleanup;
			int CLEANUP_INTERVAL = 300;

			public TargetGatingComponent(IMyShipController rc)
			{
				_rc = rc;
			}

			public void UpdateBefore()
			{
				if (DenyInertial || DenyMissingSphere > 0)
					_nG = _rc.GetNaturalGravity();
				if ((_bannedShort.Count > 0) && (E.T - _tickStampBanCleanup > CLEANUP_INTERVAL))
				{
					_tickStampBanCleanup = E.T;
					_bannedShort.Clear();
				}
			}

			public bool ConsiderAddTarget(ref MyDetectedEntityInfo info)
			{
				return !_bannedPermanent.Contains(info.EntityId) && !_bannedShort.Contains(info.EntityId);
			}

			public void BanPermanent(long id)
			{
				_bannedPermanent.Add(id);
			}

			public void ClearBans()
			{
				_bannedPermanent.Clear();
				_bannedShort.Clear();
			}

			public bool ConsiderBanning(TrackedTarget t)
			{
				string reason = null;
				if (DenySmallerThan > 0 && t.Info.BoundingBox.Extents.Length() < DenySmallerThan)
				{
					reason = "DenySmallerThan";
					_bannedPermanent.Add(t.Info.EntityId);
				}
				else if (DenyWcThreatLowerThan > 0 && t.ThreatScore < DenyWcThreatLowerThan)
				{
					reason = "DenyWcThreatLowerThan";
					_bannedPermanent.Add(t.Info.EntityId);
				}
				else if (DenyInertial && t.IsInertial(ref _nG))
				{
					reason = "DenyInertial";
					_bannedShort.Add(t.Info.EntityId);
				}
				else if (DenyMissingSphere > 0 && t.IsInertial(ref _nG) && !IsGoingToViolateMySpace(t.Info, _rc.CubeGrid.GetPosition()))
				{
					reason = "DenyMissingSphere";
					_bannedShort.Add(t.Info.EntityId);
				}

				if (reason != null)
				{
					E.DebugLog($"{t.Source}.TargetGatingComponent: banned {t.Info.EntityId}, reason: {reason}");
					return true;
				}
				return false;
			}
			bool IsGoingToViolateMySpace(MyDetectedEntityInfo mdei, Vector3D gridTrans)
			{
				var bs = new BoundingSphereD(gridTrans, DenyMissingSphere);
				return (bs.Intersects(new RayD(mdei.Position, Vector3D.Normalize(mdei.Velocity))) != null);
			}
		}


		TargetOffsetRepo TORepo;
		public class TargetOffsetRepo
		{
			Action<string> _saveToStorage;
			TargetOffsetManager _tom;
			public Action<long, List<Vector3D>> OnDefinitionMatched;

			public TargetOffsetRepo(TargetOffsetManager tom, Action<string> saveToStorage)
			{
				_saveToStorage = saveToStorage;
				_tom = tom;
			}

			public void LoadFromStorage(string storage)
			{
				E.DebugLog(storage);
			}

			public void AddMeta(ref TargetLite tl, long rootId)
			{
				if (_knownDefs.Count > 0)
				{
					if (_identifiedTargets.ContainsKey(rootId))
						tl.Name = _identifiedTargets[rootId].Name;
				}
			}

			public void ConsiderOffset(long rootId, Vector3D offset)
			{
				foreach (var def in _knownDefs.Values)
				{
					foreach (var off in def.Offsets)
					{
						if (Vector3D.Distance(offset, off) < 0.1)
						{
							_identifiedTargets[rootId] = def;
							OnDefinitionMatched?.Invoke(rootId, def.Offsets);
							//E.DebugLog("match!");
							return;
						}
					}
				}
			}

			public void SetTargetDef(string[] parts)
			{
				var tdef = CreateOrUpdate(parts);
				tdef.Offsets = new List<Vector3D>();
				int startInd = 4;
				for (int offsetZero = startInd; offsetZero + 2 < parts.Length; offsetZero += 3)
				{
					var newOffset = new Vector3D(double.Parse(parts[offsetZero]), double.Parse(parts[offsetZero + 1]), double.Parse(parts[offsetZero + 2]));
					tdef.Offsets.Add(newOffset);
				}
				E.DebugLog($"Updated target definition '{tdef.Name}', offsets: {tdef.Offsets.Count}");
			}

			// command:set-target-def:id:Name=xxx,Description=yyy:{0}:{1}:{2}:{0}:{1}:{2}
			public void SaveCurrent(string[] parts, long? targetId)
			{
				if (targetId.HasValue)
				{
					var offs = _tom.GetOffsetsForEntity(targetId.Value, true);
					if (offs != null)
					{
						var tdef = CreateOrUpdate(parts);
						tdef.Offsets = offs;
						var str = Serialize();
						E.DebugLog(str);
						_saveToStorage(str);
					}
				}
			}

			TargetDef CreateOrUpdate(string[] parts)
			{
				var id = parts[2];
				if (!_knownDefs.ContainsKey(id))
					_knownDefs.Add(id, new TargetDef());
				var tdef = _knownDefs[id];
				Dictionary<string, string> vals;
				if (ParseCmdTail(3, parts, out vals))
				{
					tdef.Name = ParseValue<string>(vals, "Name");
					tdef.Desc = ParseValue<string>(vals, "Description");
				}
				return tdef;
			}

			class TargetDef
			{
				public List<Vector3D> Offsets;
				public string Name;
				public string Desc;
			}

			Dictionary<string, TargetDef> _knownDefs = new Dictionary<string, TargetDef>();
			Dictionary<long, TargetDef> _identifiedTargets = new Dictionary<long, TargetDef>();


			string Serialize()
			{
				var lines = new List<string>();
				Func<double, double> round = x => Math.Round(x, 2);
				foreach (var def in _knownDefs)
				{
					var offs = string.Join(":", def.Value.Offsets.Select(o => VectorOpsHelper.V3DtoStringRounded(round, o)));
					lines.Add($"command:set-target-def:{def.Key}:Name={def.Value.Name},Description={def.Value.Desc}:{offs}");
				}

				return string.Join("\n", lines);
			}
		}



		TargetOffsetManager Tom = new TargetOffsetManager();
		public class TargetOffsetManager
		{
			Dictionary<long, List<Vector3D>> _raycastOffsets = new Dictionary<long, List<Vector3D>>();
			Dictionary<long, List<Vector3D>> _turretAiOffsets = new Dictionary<long, List<Vector3D>>();
			Dictionary<long, List<Vector3D>> _currentSrc;
			//int lastClearTimestamp;

			public Action<long, Vector3D> OnAddedOffset;

			public TargetOffsetManager()
			{
				_currentSrc = _turretAiOffsets;
			}

			public List<Vector3D> GetOffsetsForEntity(long id, bool? isTurret = null)
			{
				var src = _currentSrc;
				if (isTurret.HasValue)
					src = isTurret.Value ? _turretAiOffsets : _raycastOffsets;
				if (src.ContainsKey(id))
					return src[id];
				else return null;
			}

			public void ReplaceOffsetsForEntity(long id, List<Vector3D> offsets)
			{
				if (!_turretAiOffsets.ContainsKey(id))
					_turretAiOffsets.Add(id, new List<Vector3D>());
				if (!_raycastOffsets.ContainsKey(id))
					_raycastOffsets.Add(id, new List<Vector3D>());
				_turretAiOffsets[id] = offsets;
				_raycastOffsets[id] = offsets;
			}

			public int ConsiderOffset(long id, MatrixD entityRot, Vector3D entityPos, Vector3D pt, bool isTurret)
			{
				List<Vector3D> offs;
				if (isTurret)
				{
					if (!_turretAiOffsets.ContainsKey(id))
						_turretAiOffsets.Add(id, new List<Vector3D>());
					offs = _turretAiOffsets[id];
				}
				else
				{
					if (!_raycastOffsets.ContainsKey(id))
						_raycastOffsets.Add(id, new List<Vector3D>());
					offs = _raycastOffsets[id];
				}

				var offset = Vector3D.TransformNormal(pt - entityPos, MatrixD.Transpose(entityRot));
				if (!isTurret || !offs.Any(x => (offset - x).LengthSquared() < Variables.Get<float>("squared-offset-filter")))
				{
					offs.Add(offset);
					//SelectedOffsetIndex = offs.IndexOf(offset);
					OnAddedOffset?.Invoke(id, offset);
					return offs.Count - 1;
				}
				else
				{
					// getting the same for a while, clear
					//if (isTurret && (E.T - lastClearTimestamp > 1200))
					//{
					//	lastClearTimestamp = E.T;
					//	offs.Clear();
					//}
				}
				return 0;
			}

			public void ClearTurretOffsets(long id)
			{
				if (_turretAiOffsets.ContainsKey(id))
					_turretAiOffsets[id].Clear();
			}

			List<Vector3D> _tmpGetOffsets = new List<Vector3D>();
			public List<Vector3D> GetWorldOffsets(long id, MatrixD entityRot, Vector3D entityPos)
			{
				_tmpGetOffsets.Clear();
				if (_turretAiOffsets.ContainsKey(id))
					foreach (var off in _turretAiOffsets[id])
						_tmpGetOffsets.Add(Vector3D.TransformNormal(off, entityRot) + entityPos);
				if (_raycastOffsets.ContainsKey(id))
					foreach (var off in _raycastOffsets[id])
						_tmpGetOffsets.Add(Vector3D.TransformNormal(off, entityRot) + entityPos);
				return _tmpGetOffsets;
			}
			public Vector3D GetOffset(long id, int selectedOffsetIndex, MatrixD entityRot, Vector3D entityPos)
			{
				if (_currentSrc.ContainsKey(id) && _currentSrc[id].Count > 0)
				{
					var offIndex = Math.Min(_currentSrc[id].Count - 1, selectedOffsetIndex);
					return Vector3D.TransformNormal(_currentSrc[id][offIndex], entityRot) + entityPos;
				}
				else
					return entityPos;
			}
			public void RemoveOffset(long id, int selectedOffsetIndex)
			{
				if (_currentSrc.ContainsKey(id) && _currentSrc[id].Any())
				{
					var offIndex = Math.Min(_currentSrc[id].Count - 1, selectedOffsetIndex);
					_currentSrc[id].RemoveAt(offIndex);
				}
			}
			public int CycleOffset(long id, int selectedOffsetIndex)
			{
				if (_currentSrc.ContainsKey(id))
				{
					var coll = _currentSrc.First(x => x.Key == id).Value;
					for (int i = 0; i < coll.Count; i++)
					{
						if (i == selectedOffsetIndex)
						{
							var newInd = i + 1;
							if (newInd >= coll.Count)
								newInd = 0;
							return newInd;
						}
					}
				}
				return 0;
			}
			public void CycleSource()
			{
				if (_currentSrc == _turretAiOffsets)
					_currentSrc = _raycastOffsets;
				else
					_currentSrc = _turretAiOffsets;
			}
			public void SetRaycastSource()
			{
				_currentSrc = _raycastOffsets;
			}
		}
















		public struct TargetLite
		{
			public long Id;
			public string Name;
			public Vector3D? Position;
			public Vector3D? Velocity;
			public MyDetectedEntityInfo? Mdei;

			public MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> GetIgcDto()
			{
				var mask = 0 | (Velocity.HasValue ? 1 : 0) | (Mdei.HasValue ? 2 : 0) | (Mdei.HasValue ? 4 : 0);
				var x = new MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>(
						new MyTuple<string, long, byte, byte>(Name, Id, (byte)(Mdei?.Type ?? MyDetectedEntityType.LargeGrid), (byte)mask),
						Position.Value,
						Velocity ?? Vector3D.Zero,
						Mdei?.Orientation ?? MatrixD.Identity,
						Mdei?.BoundingBox ?? new BoundingBoxD()
					);
				return x;
			}
		}

		public class TargetTelemetry
		{
			public long TickStamp;
			int clock;
			public string Name;
			public string VectorCommandKey;
			public long EntityId;
			public Vector3D? Position { get; private set; }
			public Vector3D? Velocity;
			public Vector3D? Acceleration;
			public MatrixD? OrientationUnit;
			public BoundingBoxD? BoundingBox;
			public long SrcId;
			public MyDetectedEntityType? Type { get; set; }
			public TargetTelemetry(int clock, string name, string setvecCommandName)
			{
				Name = name;
				this.clock = clock;
				VectorCommandKey = setvecCommandName;
			}
			public void SetPosition(Vector3D pos)
			{
				Position = pos;
				TickStamp = E.T;
			}

			public enum TeleMetaFlags : byte
			{
				HasVelocity = 1,
				HasOrientation = 2,
				HasBB = 4
			}

			bool HasFlag(TeleMetaFlags packed, TeleMetaFlags flag)
			{
				return (packed & flag) == flag;
			}

			public void ParseIgc(long srcId, MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> igcDto, int localTick)
			{
				var meta = igcDto.Item1;
				Name = meta.Item1;
				EntityId = meta.Item2;
				Type = (MyDetectedEntityType)meta.Item3;
				TeleMetaFlags tm = (TeleMetaFlags)meta.Item4;
				var elapsed = localTick - TickStamp;
				var p = igcDto.Item2;
				if (HasFlag(tm, TeleMetaFlags.HasVelocity))
				{
					var newVel = igcDto.Item3;
					if (!Velocity.HasValue)
						Velocity = newVel;
					if (elapsed > 0)
						Acceleration = (newVel - Velocity.Value) * 60 / elapsed;
					Velocity = newVel;
					p += newVel * E.Dt;
				}
				SetPosition(p);
				if (HasFlag(tm, TeleMetaFlags.HasOrientation))
					OrientationUnit = igcDto.Item4;
				if (HasFlag(tm, TeleMetaFlags.HasBB))
					BoundingBox = igcDto.Item5;
				SrcId = srcId;
				metrics.ParseVectorsCount++;
			}

			public static TargetTelemetry FromIgc(long srcId, string apCKcmd, MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> igcDto, int localTick)
			{
				var t = new TargetTelemetry(1, igcDto.Item1.Item1, apCKcmd);
				t.ParseIgc(srcId, igcDto, E.T);
				return t;
			}

			public MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD> GetIgcDto()
			{
				var mask = 0 | (Velocity.HasValue ? 1 : 0) | (OrientationUnit.HasValue ? 2 : 0) | (BoundingBox.HasValue ? 4 : 0);
				var x = new MyTuple<MyTuple<string, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>(
						new MyTuple<string, long, byte, byte>(Name, EntityId, (byte)MyDetectedEntityType.LargeGrid, (byte)mask),
						Position.Value,
						Velocity ?? Vector3D.Zero,
						OrientationUnit ?? MatrixD.Identity,
						BoundingBox ?? new BoundingBoxD()
					);
				return x;
			}

			public void Invalidate()
			{
				Position = null;
				Velocity = null;
				OrientationUnit = null;
				BoundingBox = null;
				TickStamp = 0;
			}
		}

		public static Metrics metrics;
		public struct Metrics
		{
			public int ParseVectorsCount;
			public int ScanBurstsCount;
			public int RcvMsg;
		}

		public static class VectorOpsHelper
		{
			public static string V3DtoBroadcastString(params Vector3D[] vectors)
			{
				return string.Join(":", vectors.Select(v => string.Format("{0}:{1}:{2}", v.X, v.Y, v.Z)));
			}

			public static string V3DtoStringRounded(Func<double, double> rounding, params Vector3D[] vectors)
			{
				return string.Join(":", vectors.Select(v => string.Format("{0}:{1}:{2}", rounding(v.X), rounding(v.Y), rounding(v.Z))));
			}

			public static Vector3D GetAnglesToPointTrig(Vector3D targetPosition, IMyTerminalBlock pivotBlock)
			{
				Vector3D targetNorm = Vector3D.Normalize(targetPosition - pivotBlock.GetPosition());
				double yawCorrectionRads = Math.Acos(Vector3D.Dot(pivotBlock.WorldMatrix.Left, targetNorm));
				double targetYawGrads = yawCorrectionRads * 180 / Math.PI - 90;
				double targetPitchGrads = Math.Acos(Vector3D.Dot(pivotBlock.WorldMatrix.Up, targetNorm)) * 180 / Math.PI - 90;
				return new Vector3D(targetYawGrads, -targetPitchGrads, 0);
			}

			public static Vector3D GetAnglesToPoint(Vector3D targetPosition, IMyTerminalBlock pivotBlock, MatrixD fwGyroDefault)
			{
				// only for turret!
				var desM = MatrixD.CreateFromDir(Vector3D.Normalize(targetPosition - pivotBlock.WorldMatrix.Translation), pivotBlock.WorldMatrix.Up);
				var myFrameRot = pivotBlock.WorldMatrix.GetOrientation();
				var gatp = GetAnglesToPointMrot(desM, myFrameRot, fwGyroDefault);
				return new Vector3D(-gatp.X, gatp.Y, gatp.Z);
			}

			public static Vector3D GetAnglesToPointMrot(MatrixD desiredRot, MatrixD myFrameRot, MatrixD fwGyroDefault)
			{
				var trans = desiredRot * MatrixD.Invert(myFrameRot);
				Vector3D a;
				MatrixD.GetEulerAnglesXYZ(ref trans, out a);
				a = Vector3D.TransformNormal(a, fwGyroDefault * MatrixD.Invert(myFrameRot));
				return a * 180 / Math.PI;
			}

		}

		public class IncomingMessage
		{
			public string Msg { get; set; }
			public long From { get; set; }
		}

		IEnumerable<IncomingMessage> ParseMessage(string msg, long sender)
		{
			var items = msg.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('[', ']')).ToList();
			foreach (var item in items)
			{
				yield return new IncomingMessage { Msg = item, From = sender };
			}
		}

		IEnumerable<IncomingMessage> ParseMessage(string msg)
		{
			var items = msg.Split(new[] { "],[" }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim('[', ']')).ToList();
			foreach (var item in items)
			{
				//string msg = "{recipient-types:Drone+Missile, recipient-ids: 12313+10, sender-id:H }command:handshake";
				if (item.StartsWith("{"))
				{
					string[] parts = item.Split('}');
					string body = parts[1];
					string headers = parts[0].TrimStart('{');
					string from = "";
					yield return new IncomingMessage { Msg = body, From = long.Parse(from) };
				}
				else
				{
					yield return new IncomingMessage { Msg = item };
				}
			}
		}

		public void UnicastCommandAutoPillock(string cmd)
		{
			if (APckCoreId.HasValue)
			{
				if (IGC.IsEndpointReachable(APckCoreId.Value))
				{
					E.DebugLog($"Sending apck command '{cmd}'");
					IGC.SendUnicastMessage(APckCoreId.Value, "apck.command", cmd);
				}
				else
				{
					E.DebugLog($"APck {APckCoreId.Value} is not reachable");
				}
			}
			else
			{
				E.DebugLog("APck core PB not found");
			}
		}

		class Scheduler
		{
			static Scheduler inst = new Scheduler();
			Scheduler() { }

			public static Scheduler C
			{
				get
				{
					inst.delayForNextCmd = 0;
					inst.repeatCondition = null;
					return inst;
				}
			}

			class DelayedCommand
			{
				public DateTime TimeStamp;
				//public string Command;
				public Action Command;
				public Func<bool> repeatCondition;
				public long delay;
			}

			Queue<DelayedCommand> q = new Queue<DelayedCommand>();
			long delayForNextCmd;
			Func<bool> repeatCondition;

			public Scheduler After(int ms)
			{
				this.delayForNextCmd += ms;
				return this;
			}

			public Scheduler RunCmd(Action cmd)
			{
				q.Enqueue(new DelayedCommand { TimeStamp = DateTime.Now.AddMilliseconds(delayForNextCmd), Command = cmd, repeatCondition = repeatCondition, delay = delayForNextCmd });
				return this;
			}

			public Scheduler RepeatWhile(Func<bool> repeatCondition)
			{
				this.repeatCondition = repeatCondition;
				return this;
			}

			public void HandleTick()
			{
				if (q.Count > 0)
				{
					E.Echo("Scheduled actions count:" + q.Count);
					var c = q.Peek();
					if (c.TimeStamp < DateTime.Now)
					{
						if (c.repeatCondition != null)
						{
							if (c.repeatCondition.Invoke())
							{
								c.Command.Invoke();
								c.TimeStamp = DateTime.Now.AddMilliseconds(c.delay);
							}
							else
							{
								q.Dequeue();
							}
						}
						else
						{
							c.Command.Invoke();
							q.Dequeue();
						}

						//sendFeedback("Executing " + c.Command, "", true);
					}
				}
			}
		}


		InputListener UserCtrlListener;
		public class InputListener
		{
			public Vector2 Cumulative = new Vector2();
			List<IMyShipController> ctrls;
			Func<int> tickGetter;
			public IMyShipController GetControlledCockpit()
			{
				return _activeController;
			}

			public IMyShipController GetLastNonRemoteController()
			{
				return _lastActiveController;
			}

			IMyShipController _activeController;
			IMyShipController _lastActiveController;
			Vector3 _moveInd;
			Vector2 _rotInd;
			float _rollInd;

			public void PrepareBeforeTick()
			{
				foreach (var x in h)
					x.Update();
				_activeController = ctrls.Where(c => c.IsUnderControl).FirstOrDefault();

				if (_activeController != null)
				{
					_moveInd = _activeController.MoveIndicator;
					_moveInd.X = -_moveInd.X;
					//_moveInd.Z = -_moveInd.Z;
					if (!(_activeController is IMyRemoteControl))
						_lastActiveController = _activeController;
				}
				else
					_moveInd = Vector3.Zero;

				_rollInd = _activeController?.RollIndicator ?? 0f;
				_rotInd = _activeController?.RotationIndicator ?? Vector2.Zero;
			}

			public Vector3 GetVector()
			{
				return _moveInd;
			}
			public Vector3D GetUserCtrlVector(ref MatrixD fwRef)
			{
				Vector3 res = new Vector3();
				if (Toggle.C.Check("ignore-user-thruster"))
					return res;
				if ((_activeController != null) && (_activeController.MoveIndicator != Vector3.Zero))
					return Vector3D.TransformNormal((Vector3D)_activeController.MoveIndicator, fwRef * MatrixD.Transpose(_activeController.WorldMatrix));
				return res;
			}
			public Vector2 GetRot()
			{
				return _rotInd;
			}
			public float GetRoll()
			{
				return _rollInd;
			}

			class InputHistory
			{
				public string KeyName;
				public int LastKeyDownStamp;
				public int State { get; private set; }
				public int PendingState;
				public void Update()
				{
					//if (State != PendingState)
						//E.DebugLog($"Key state [{KeyName}]: {State} -> {PendingState}");
					State = PendingState;
				}
			}

			List<InputHistory> h;
			public InputListener(List<IMyShipController> ctrlToConsider, Func<int> tickGetter)
			{
				ctrls = ctrlToConsider;
				this.tickGetter = tickGetter;

				h = new List<InputHistory>();
				h.Add(new InputHistory { KeyName = "spacebar" });
				h.Add(new InputHistory { KeyName = "c" });
				h.Add(new InputHistory { KeyName = "e" });
				h.Add(new InputHistory { KeyName = "q" });

				h.Add(new InputHistory { KeyName = "w" });
				h.Add(new InputHistory { KeyName = "s" });
				h.Add(new InputHistory { KeyName = "a" });
				h.Add(new InputHistory { KeyName = "d" });
			}

			public bool CheckKeyDown(string keyName)
			{
				if (_activeController != null)
				{
					bool isKeyDown = false;
					if ((keyName == "spacebar") && (_moveInd.Y > 0))
						isKeyDown = true;
					if ((keyName == "c") && (_moveInd.Y < 0))
						isKeyDown = true;
					if ((keyName == "e") && (_rollInd > 0))
						isKeyDown = true;
					if ((keyName == "q") && (_rollInd < 0))
						isKeyDown = true;
					if ((keyName == "w") && (_moveInd.Z < 0))
						isKeyDown = true;
					if ((keyName == "s") && (_moveInd.Z > 0))
						isKeyDown = true;
					if ((keyName == "a") && (_moveInd.X > 0))
						isKeyDown = true;
					if ((keyName == "d") && (_moveInd.X < 0))
						isKeyDown = true;

					return isKeyDown;
				}
				return false;
			}

			public bool KeyReleased(string keyName)
			{
				var cState = h.First(h => h.KeyName == keyName);
				if (CheckKeyDown(keyName))
				{
					if (cState.State == 0)
					{
						cState.PendingState = 1;
						cState.LastKeyDownStamp = tickGetter();
					}
					if (cState.State == 2)
					{
						cState.PendingState = 0;
					}
					return false;
				}
				else
				{
					if ((cState.State == 1) || (cState.State == 2))
					{
						cState.PendingState = 0;
						return true;
					}
				}
				return false;
			}
			public bool CheckDoubleTap(string keyName)
			{
				return UpdateKeyState(keyName, tickGetter(), CheckKeyDown(keyName));
			}

			bool UpdateKeyState(string keyName, int currentTick, bool keyDown)
			{
				var cState = h.First(h => h.KeyName == keyName);
				if (keyDown)
				{
					if (cState.State == 0)
					{
						cState.PendingState = 1;
						cState.LastKeyDownStamp = currentTick;
					}
					if (cState.State == 2) // was released less than 30 ticks ago
					{
						cState.PendingState = 0;
						return true;
					}
				}
				else
				{
					if (currentTick - cState.LastKeyDownStamp < 30)
					{
						if (cState.State == 1)
						{
							cState.PendingState = 2;
						}
					}
					else
					{
						cState.PendingState = 0;
					}
				}
				return false;
			}
		}

		List<MyTuple<string, Vector3D, ImmutableArray<string>>> prjs = new List<MyTuple<string, Vector3D, ImmutableArray<string>>>();
		void EmitProjection(string tag, Vector3D p, params string[] s)
		{
			prjs.Add(new MyTuple<string, Vector3D, ImmutableArray<string>>(tag, p, s.ToImmutableArray()));
		}

		void EmitFlush(long addr)
		{
			IGC.SendUnicastMessage(addr, "hud.tgp.proj", prjs.ToImmutableArray());
			prjs.Clear();
		}

		void EmitHudText(long a, string t, Vector2 p, float size)
		{
			IGC.SendUnicastMessage(a, "draw-text",
						new MyTuple<string, Vector2, float>(
						t,
						p,
						size
					));
		}

		static bool ParseCmdTail(int ind, string[] parts, out Dictionary<string, string> vals)
		{
			if ((parts.Length > ind) && parts[3].Contains("="))
			{
				vals = parts[ind].Split(',').ToDictionary(s => s.Split('=')[0], s => s.Split('=')[1]);
				return true;
			}
			vals = null;
			return false;
		}

		static T ParseValue<T>(Dictionary<string, string> values, string key)
		{
			string res;
			if ((values != null) && values.TryGetValue(key, out res) && !string.IsNullOrEmpty(res))
			{
				if (typeof(T) == typeof(string))
					return (T)(object)res;
				else if (typeof(T) == typeof(int?))
					return (T)(object)int.Parse(res);
				else if (typeof(T) == typeof(long?))
					return (T)(object)long.Parse(res);
				else if (typeof(T) == typeof(float?))
					return (T)(object)float.Parse(res);
			}
			return default(T);
		}

		public static class E
		{
			static string debugTag = "";
			static Action<string> e;
			static IMyTextSurface p;
			static IMyTextPanel ech;
			static IMyTextSurface l;
			static IMyIntergridCommunicationSystem _i;
			public static int T;
			public static double Dt;
			public static int ErrCtr;
			public static void Init(Action<string> echo, IMyGridTerminalSystem g, IMyProgrammableBlock me, IMyIntergridCommunicationSystem i)
			{
				e = echo;
				p = me.GetSurface(0);
				p.ContentType = ContentType.TEXT_AND_IMAGE;
				p.WriteText("");
				ech = g.GetBlockWithName("LCD Echo") as IMyTextPanel;
				_i = i;
			}
			public static void Echo(string s)
			{
				if ((debugTag == "") || s.Contains(debugTag))
					e(s);
				if (ech != null)
					DebugToPanel(s);
			}

			static string buff = "";
			public static void DebugToPanel(string s)
			{
				buff += s + "\n";
			}
			static List<string> linesToLog = new List<string>();
			public static void DebugLog(string s)
			{
				p.WriteText($"{T}: {s}\n", true);
				if (l != null)
				{
					linesToLog.Add(s);
				}
			}
			public static void Fail(string s)
			{
				ErrCtr++;
				DebugLog(s);
			}
			public static void ClearLog()
			{
				l?.WriteText("");
				linesToLog.Clear();
			}
			public static void AddLogger(IMyTextSurface s)
			{
				l = s;
			}
			public static void EndOfTick()
			{
				if (!string.IsNullOrEmpty(buff))
				{
					ech?.WriteText(buff);
					buff = "";
				}
				if (linesToLog.Count > 0)
				{
					if (l != null)
					{
						linesToLog.Reverse();
						var t = string.Join("\n", linesToLog) + "\n" + l.GetText();
						var u = LOGGER_MAX_CHARS;
						if (t.Length > u)
							t = t.Substring(0, u - 1);
						l.WriteText($"{T:f2}: {t}");
					}
					linesToLog.Clear();
				}
				if (_drCalls.Count > 0)
				{
					//_i.SendBroadcastMessage("cmdr.persist-projection.batch", _drCalls.ToImmutableArray());
					_i.SendBroadcastMessage("cmdr.draw-projection.batch", _drCalls.ToImmutableArray());
					_drCalls.Clear();
				}
			}


			static List<MyTuple<string, Vector2, Vector3D, Vector4, string>> _drCalls = new List<MyTuple<string, Vector2, Vector3D, Vector4, string>>();
			public static void Draw(Vector3D p, string spr, string cs, int size, string lbl)
			{
				var rgb = cs.Trim('#');
				var c = new Color(GetFromHex(rgb, 0), GetFromHex(rgb, 2), GetFromHex(rgb, 4));
				_drCalls.Add(new MyTuple<string, Vector2, Vector3D, Vector4, string>(spr, Vector2.One * size, p, c.ToVector4(), lbl));
			}
			static int GetFromHex(string x, int i)
			{
				return int.Parse(x.Substring(i, 2), System.Globalization.NumberStyles.HexNumber);
			}

			public static Action<string> Info;
			public static Action InfoOnTickStart;
			public static Action<string> Lock;
			public static Action LockOnTickStart;
		}
	

