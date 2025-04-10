using System;
using System.Collections.Generic;
using System.Linq;
using Sandbox.Game.Entities;
using Sandbox.Game.Localization;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Ingame;
using VRage;
using VRage.Game;
using VRage.Game.Entity;
using VRage.Utils;
using VRageMath;
using VRage.Game.ModAPI;
using VRage.Library.Utils;
using MyDetectedEntityInfo = Sandbox.ModAPI.Ingame.MyDetectedEntityInfo;
using IMyProgrammableBlock = Sandbox.ModAPI.Ingame.IMyProgrammableBlock;
using TupleRadarEntry = VRage.MyTuple<uint, string, VRage.MyTuple<VRageMath.Vector3, VRageMath.Vector3, float, float>, long, byte>;
using TupleRadarPositionData = VRage.MyTuple<VRageMath.Vector3, VRageMath.Vector3, float, float>;

namespace GrubenRadarApi
{
    public partial class RadarApiMod
    {
        public MyGameTimer Timer;
        static readonly Random Rand = new Random();
        
        //Boring API init/uninit stuff
        void SetupApi()
        {
            PBInterface.AddMethod("GetDetectedRadarTargets", new Func<IMyProgrammableBlock, List<MyDetectedEntityInfo>>(API_GetDetectedRadarTargets));
            MyLog.Default.WriteLine("Setting up Radar API");
            Timer = new MyGameTimer();
        }
        void RegisterRadarApi()
        {
            MyAPIUtilities.Static.RegisterMessageHandler(3290983434, OnRadarAPIReady);
        }

        void UnregisterRadarApi()
        {
            MyAPIUtilities.Static.UnregisterMessageHandler(3290983434, OnRadarAPIReady);
        }

        void OnRadarAPIReady(object obj)
        {
            var dict = obj as Dictionary<string, Delegate>;
            if (dict != null)
            {
                NerdRadarAPI = dict;
            }
        }
        //The Actual Method(s)
        List<MyDetectedEntityInfo> API_GetDetectedRadarTargets(IMyProgrammableBlock pb)
        {
            IMyCubeGrid grid = (IMyCubeGrid)pb.CubeGrid;
            var getRadarEntries = (Func<IMyCubeGrid, List<TupleRadarEntry>>)NerdRadarAPI["GetAllRadarEntries"];
            List<TupleRadarEntry> radarEntries = getRadarEntries(grid);
            List<MyDetectedEntityInfo> result = new List<MyDetectedEntityInfo>();
            foreach (TupleRadarEntry entry in radarEntries)
            {
                uint trackNumber = entry.Item1;
                string name = entry.Item2;
                Vector3 position = entry.Item3.Item1;
                Vector3 velocity = entry.Item3.Item2;
                float posError = entry.Item3.Item3;
                float velError = entry.Item3.Item4;
                long mainGridEntityId = entry.Item4;
                byte relation = entry.Item5;

                MyDetectedEntityInfo target = CustomCreateHelper(
                    (MyEntity)MyAPIGateway.Entities.GetEntityById(mainGridEntityId), grid.BigOwners.FirstOrDefault(), posError, velError);
                result.Add(target);
            }
            return result;
        }

        //50% of this is copied from the API method, don't ask my why or how this works. Because it really should not.
        MyDetectedEntityInfo CustomCreateHelper(MyEntity entity, long sensorOwner, float posError, float velError)
        {
            if (entity == null)
                return new MyDetectedEntityInfo();
            
            MatrixD orientation1 = MatrixD.Zero;
            Vector3 velocity = Vector3.Zero;
            int timeInMilliseconds = Timer.ElapsedTimeSpan.Milliseconds;
            BoundingBoxD worldAabb1 = entity.PositionComp.WorldAABB;
            
            if (entity.Physics != null)
            {
                orientation1 = entity.Physics.GetWorldMatrix().GetOrientation();
                velocity = entity.Physics.LinearVelocity;
            }
            if (entity.GetTopMostParent() != null && entity.GetTopMostParent().GetType() == typeof(MyCubeGrid))
            {
                MyCubeGrid topMostParent = (MyCubeGrid)entity.GetTopMostParent();
                MyDetectedEntityType type = topMostParent.GridSizeEnum != MyCubeSize.Small ? MyDetectedEntityType.LargeGrid : MyDetectedEntityType.SmallGrid;
                MyRelationsBetweenPlayerAndBlock relationship = topMostParent.BigOwners.Count != 0 ? MyIDModule.GetRelationPlayerBlock(sensorOwner, topMostParent.BigOwners[0], MyOwnershipShareModeEnum.Faction) : MyRelationsBetweenPlayerAndBlock.NoOwnership;
                string name = relationship == MyRelationsBetweenPlayerAndBlock.Owner || relationship == MyRelationsBetweenPlayerAndBlock.FactionShare || relationship == MyRelationsBetweenPlayerAndBlock.Friends ? topMostParent.DisplayName : (topMostParent.GridSizeEnum != MyCubeSize.Small ? MyTexts.GetString(MySpaceTexts.DetectedEntity_LargeGrid) : MyTexts.GetString(MySpaceTexts.DetectedEntity_SmallGrid));
                MatrixD orientation2 = topMostParent.WorldMatrix.GetOrientation();
                Vector3 linearVelocity = topMostParent.Physics.LinearVelocity + new Vector3(Rand.NextDouble()* velError, Rand.NextDouble()* velError, Rand.NextDouble()* velError);
                BoundingBoxD worldAabb2 = new BoundingBoxD(topMostParent.PositionComp.WorldAABB.Min + new Vector3(Rand.NextDouble()* posError, Rand.NextDouble()* posError, Rand.NextDouble()* posError), topMostParent.PositionComp.WorldAABB.Max  + new Vector3(Rand.NextDouble()* posError, Rand.NextDouble()* posError, Rand.NextDouble()* posError));
                    //topMostParent.PositionComp.WorldAABB;
                return new MyDetectedEntityInfo(topMostParent.EntityId, name, type, null, orientation2, linearVelocity, relationship, worldAabb2, (long) timeInMilliseconds);
            }
            return new MyDetectedEntityInfo();
        }
        

    }
}
