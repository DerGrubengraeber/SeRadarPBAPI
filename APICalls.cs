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
        private static readonly MyRandom Random = new MyRandom();
        
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
                    (MyEntity)MyAPIGateway.Entities.GetEntityById(mainGridEntityId), grid.BigOwners.FirstOrDefault(), posError, velError, name);
                result.Add(target);
            }
            return result;
        }

        //50% of this is copied from the API method, the other 50% was revealed to me in a dream. Don't ask me why or how this works. Because it really should not.
        MyDetectedEntityInfo CustomCreateHelper(MyEntity entity, long sensorOwner, float posError, float velError, string name)
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
                //string name = relationship == MyRelationsBetweenPlayerAndBlock.Owner || relationship == MyRelationsBetweenPlayerAndBlock.FactionShare || relationship == MyRelationsBetweenPlayerAndBlock.Friends ? topMostParent.DisplayName : (topMostParent.GridSizeEnum != MyCubeSize.Small ? MyTexts.GetString(MySpaceTexts.DetectedEntity_LargeGrid) : MyTexts.GetString(MySpaceTexts.DetectedEntity_SmallGrid));
                MatrixD orientation2 = topMostParent.WorldMatrix.GetOrientation();
                //Vector3 linearVelocity = topMostParent.Physics.LinearVelocity + new Vector3(Rand.NextDouble()* velError, Rand.NextDouble()* velError, Rand.NextDouble()* velError);
                Vector3 posOffset = GetRandomOffset(posError);
                BoundingBoxD worldAabb2 = new BoundingBoxD(
                    topMostParent.PositionComp.WorldAABB.Min + posOffset,
                    topMostParent.PositionComp.WorldAABB.Max + posOffset
                );
                Vector3 velOffset = GetRandomOffset(velError);
                Vector3 linearVelocity = topMostParent.Physics != null
                    ? topMostParent.Physics.LinearVelocity + velOffset
                    : velOffset;
                    //topMostParent.PositionComp.WorldAABB;
                return new MyDetectedEntityInfo(topMostParent.EntityId, name, type, null, orientation2, linearVelocity, relationship, worldAabb2, (long) timeInMilliseconds);
            }
            return new MyDetectedEntityInfo();
        }
        
        Vector3 GetRandomOffset(float maxOffset)
        {
            if (maxOffset == 0.0f)
                return Vector3.Zero;
            
            Vector3 offset = Vector3.One;
            int attempt = 0;
            while (offset.LengthSquared() > 1f && attempt < 100)
            {
                offset.X = Random.GetRandomFloat(-1f, 1f);
                offset.Y = Random.GetRandomFloat(-1f, 1f);
                offset.Z = Random.GetRandomFloat(-1f, 1f);
                attempt++;
            }
            offset.Normalize();
            return offset * Random.GetRandomFloat(0, 10 * maxOffset);
        }
        

    }
}
