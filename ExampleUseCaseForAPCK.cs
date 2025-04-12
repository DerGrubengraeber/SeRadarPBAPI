// -- CONFIGURATION --
		int repeateTicks = 40;  // Age of Message in Ticks after which a Grid is allowed to be sent
		bool debug = true;
		// -------------------

		GrubenRadarAPI RadarAPI;
		bool activated = false;


		public Program()
		{

			Runtime.UpdateFrequency = UpdateFrequency.Update10;
			if (!activated)
			{
				try
				{
					RadarAPI = new GrubenRadarAPI(this);
					activated = true;
				}
				catch
				{
					activated = false;
				}
			}

			E.Init(s => Echo(s), GridTerminalSystem, Me);

			var gr = GetThisBlockGroup(Me);
			if (gr == null)
			{
				Runtime.UpdateFrequency = UpdateFrequency.None;
				Echo("Can't find hardware group containing this PB, stopping now.");
			}
			else
			{
				var rc = new List<IMyRemoteControl>();
				gr.GetBlocksOfType(rc);
				RemCon = rc.First();

				_isLargeGrid = Me.CubeGrid.GridSizeEnum == VRage.Game.MyCubeSize.Large;

				MyIni _ini = new MyIni();
				MyIniParseResult result;
				if (!string.IsNullOrEmpty(Me.CustomData))
				{
					if (!_ini.TryParse(Me.CustomData, out result))
						throw new Exception("CustomData ini fail");
					else
					{
						Vector3D offset = Vector3D.Zero;
						offset.Y = _ini.Get("emitter", "y").ToSingle(0);
						offset.Z = _ini.Get("emitter", "z").ToSingle(0);
						offset.X = _ini.Get("emitter", "x").ToSingle(0);
						transOffset = offset;
					}
				}

				caster = new TargetCasterP2P(IGC, targets);

				E.DebugLog($"command:create-task:ram:TargetId={IGC.Me}");
				E.DebugLog($"command:create-task:attack:TargetId={IGC.Me}");
			}
		}

		bool _isLargeGrid;

		IMyRemoteControl RemCon;

		Vector3D? transOffset;

		List<MyIGCMessage> igcMsgs = new List<MyIGCMessage>();
		List<TargetLite> targets = new List<TargetLite>();
		List<MyDetectedEntityInfo> Targets = new List<MyDetectedEntityInfo>();

		void Main(string param, UpdateType updateType)
		{
			if (!activated)
			{
				try
				{
					activated = true;
				}
				catch
				{
					activated = false;
				}
			}
			try
			{
				E.tick++;
				E.Dt = Math.Max(0.001, Runtime.TimeSinceLastRun.TotalSeconds);
				E.T += E.Dt;

				igcMsgs.Clear();
				while (IGC.UnicastListener.HasPendingMessage)
					igcMsgs.Add(IGC.UnicastListener.AcceptMessage());

				targets.Clear();

				var pos = RemCon.Closed ? Me.CubeGrid.WorldMatrix.Translation : RemCon.WorldMatrix.Translation;
				if (transOffset.HasValue && !RemCon.Closed)
					pos += Vector3D.Rotate(transOffset.Value, RemCon.WorldMatrix);
				var vel = RemCon.GetShipVelocities().LinearVelocity;
				var aabb = Me.WorldAABB; // scale if needed
				var wm = RemCon.Closed ? Me.CubeGrid.WorldMatrix : RemCon.WorldMatrix;

				E.Init(s => Echo(s), GridTerminalSystem, Me);
Targets.Clear();

                Targets = RadarAPI.GetDetectedRadarTargets(this.Me);

                List<MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>> targetData = new List<MyTuple<MyTuple<string, long, long, byte, byte>, Vector3D, Vector3D, MatrixD, BoundingBoxD>>();

				List<MyDetectedEntityInfo> TargetsUse = new List<MyDetectedEntityInfo>();
	foreach (MyDetectedEntityInfo info in Targets)
{
if (debug) Echo("Send Entity Data of '" + info.Name);
					if (info.Relationship == MyRelationsBetweenPlayerAndBlock.Enemies) TargetsUse.Add(info);
}

				foreach (MyDetectedEntityInfo info in TargetsUse)
				{
					var target = new TargetLite
					{
						Mdei = info,
						Id = info.EntityId,
						Name = info.Name,
						Position = info.Position,
						Velocity = info.Velocity
					};
					targets.Add(target);
				}

				caster.Handle(igcMsgs);
			}
			catch (Exception ex)
			{
				E.DebugLog(ex.ToString());
				throw;
			}

			Echo($"{Runtime.LastRunTimeMs:f3}");
		}

		TargetCasterP2P caster;
		public class TargetCasterP2P
		{
			IMyIntergridCommunicationSystem _igc;
			IMyBroadcastListener _b;
			IMyBroadcastListener _bWho;
			IMyBroadcastListener _bWhoPredicate;
			List<TargetLite> _targets;

			public enum TargetSelection { First, Closest, Random, Loop }

			public TargetCasterP2P(IMyIntergridCommunicationSystem igc, List<TargetLite> tgts)
			{
				_igc = igc;
				_b = igc.RegisterBroadcastListener("apck.unicast.closed"); // key
				_bWho = igc.RegisterBroadcastListener("apck.unicast.whohas"); // key, dataId
				_bWhoPredicate = igc.RegisterBroadcastListener("apck.unicast.whohas+predicate");
				_targets = tgts;

				Init();
			}

			List<TargetLite> _selectionBuffer = new List<TargetLite>();
			Random _r = new Random();
			int _ctr;

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
						foreach (var s in _subscriptions)
							s.Value.Remove(src);
					}
					else
						_subscriptions[id].Remove(src);
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
								i = _r.Next(_selectionBuffer.Count - 1);
							if (ts == TargetSelection.Loop)
								i = _ctr++ % _selectionBuffer.Count;
							return _selectionBuffer[i].Id;
						}
					}
					return -1;
				};

				tgp.Send = () =>
				{
					foreach (var tt in _targets)
					{
						var offer = new TargetOffer { EntityId = tt.Id, Position = tt.Position.Value };
						_igc.SendBroadcastMessage("tgp.global.gridsense.offer", offer.GetIgcDto(), TransmissionDistance.AntennaRelay);

						if (_subscriptions.ContainsKey(tt.Id))
						{
							var subs = _subscriptions[tt.Id];
							if (subs.Count > 0)
							{
								E.Echo($"Tg: {tt.Id.ToString().Substring(0, 4)}...: {subs.Count}");
								var dto = tt.GetIgcDto();
								foreach (var s in subs)
								{
									_igc.SendUnicastMessage(s, "tgp.local.gridsense.update", dto);
								}
							}
							else
								_subscriptions.Remove(tt.Id);
						}

						_igc.SendBroadcastMessage("tgp.global.gridsense.update", tt.GetIgcDto(), TransmissionDistance.AntennaRelay);
					}
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

			Dictionary<long, HashSet<long>> _subscriptions = new Dictionary<long, HashSet<long>>();
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
					if (m.Tag.Contains("apck.unicast.t"))
					{
						var d = (MyTuple<string, long>)m.Data;
						var sKey = d.Item1;
						var dataId = d.Item2;
						if (_proxies.ContainsKey(sKey))
						{
							var s = _proxies[sKey];
							if (m.Tag == "apck.unicast.t+")
								s.AddSub.Invoke(m.Source, dataId);
							else if (m.Tag == "apck.unicast.t-")
								s.RemoveSub.Invoke(m.Source, dataId);
						}
					}
				}

				foreach (var x in _proxies)
					x.Value.Send();
			}

			public struct TargetOffer
			{
				public long EntityId;
				public Vector3D Position;
				public MyTuple<long, Vector3D> GetIgcDto()
				{
					return new MyTuple<long, Vector3D>(EntityId, Position);
				}
			}
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

		public static class E
		{
			static string debugTag = "";
			static Action<string> e;
			static IMyTextSurface p;
			public static int tick;
			public static double T;
			public static double Dt;
			public static void Init(Action<string> echo, IMyGridTerminalSystem g, IMyProgrammableBlock me)
			{
				e = echo;
				p = me.GetSurface(0);
				p.ContentType = ContentType.TEXT_AND_IMAGE;
				p.WriteText("");
			}
			public static void Echo(string s)
			{
				if ((debugTag == "") || s.Contains(debugTag))
					e(s);
			}

			public static void DebugLog(string s)
			{
				p.WriteText($"{E.tick}: {s}\n", true);
			}
		}
		
		public class GrubenRadarAPI
{
    public readonly bool ModDetected;

    Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>> _gtrt;
    IMyProgrammableBlock _pb;

    public GrubenRadarAPI(MyGridProgram program)
    {
        if (program == null)
            throw new Exception("Pass `this` into the API constructor.");

        _pb = program.Me;

        var prop = _pb.GetProperty("GrubenRadarAPI");
        if (prop == null)
            return;

        var methods = prop.As<IReadOnlyDictionary<string, Delegate>>().GetValue(_pb);
        if (methods == null)
            return;

        Delegate rawDelegate;
        if (!methods.TryGetValue("GetDetectedRadarTargets", out rawDelegate))
            return;

        _gtrt = rawDelegate as Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>>;
        ModDetected = _gtrt != null;
    }

    public List<MyDetectedEntityInfo> GetDetectedRadarTargets(IMyProgrammableBlock pb)
    {
        if (_gtrt == null || pb == null)
            return new List<MyDetectedEntityInfo>();

        return _gtrt.Invoke(pb);
    }
}
		
