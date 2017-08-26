﻿using System.Collections.Generic;
using OpenSage.Data.Ini;
using OpenSage.Data.Ini.Parser;

namespace OpenSage.Logic.Object
{
    public sealed class OCLSpecialPower : ObjectBehavior
    {
        internal static OCLSpecialPower Parse(IniParser parser) => parser.ParseBlock(FieldParseTable);

        private static readonly IniParseTable<OCLSpecialPower> FieldParseTable = new IniParseTable<OCLSpecialPower>
        {
            { "SpecialPowerTemplate", (parser, x) => x.SpecialPowerTemplate = parser.ParseAssetReference() },
            { "OCL", (parser, x) => x.OCL = parser.ParseAssetReference() },
            { "UpgradeOCL", (parser, x) => x.UpgradeOCLs.Add(UpgradeOCL.Parse(parser)) },
            { "CreateLocation", (parser, x) => x.CreateLocation = parser.ParseEnum<OCLCreationPoint>() },
            { "StartsPaused", (parser, x) => x.StartsPaused = parser.ParseBoolean() },
            { "ScriptedSpecialPowerOnly", (parser, x) => x.ScriptedSpecialPowerOnly = parser.ParseBoolean() },
            { "OCLAdjustPositionToPassable", (parser, x) => x.OCLAdjustPositionToPassable = parser.ParseBoolean() },
            { "ReferenceObject", (parser, x) => x.ReferenceObject = parser.ParseAssetReference() },
        };

        public string SpecialPowerTemplate { get; private set; }
        public string OCL { get; private set; }
        public List<UpgradeOCL> UpgradeOCLs { get; } = new List<UpgradeOCL>();
        public OCLCreationPoint CreateLocation { get; private set; }
        public bool StartsPaused { get; private set; }

        [AddedIn(SageGame.CncGeneralsZeroHour)]
        public bool ScriptedSpecialPowerOnly { get; private set; }

        [AddedIn(SageGame.CncGeneralsZeroHour)]
        public bool OCLAdjustPositionToPassable { get; private set; }

        [AddedIn(SageGame.CncGeneralsZeroHour)]
        public string ReferenceObject { get; private set; }
    }

    public sealed class UpgradeOCL
    {
        internal static UpgradeOCL Parse(IniParser parser)
        {
            return new UpgradeOCL
            {
                Science = parser.ParseAssetReference(),
                OCL = parser.ParseAssetReference()
            };
        }

        public string Science { get; private set; }
        public string OCL { get; private set; }
    }

    public enum OCLCreationPoint
    {
        [IniEnum("USE_OWNER_OBJECT")]
        UseOwnerObject,

        [IniEnum("CREATE_AT_EDGE_NEAR_SOURCE")]
        CreateAtEdgeNearSource,

        [IniEnum("CREATE_AT_EDGE_FARTHEST_FROM_TARGET")]
        CreateAtEdgeFarthestFromTarget,

        [IniEnum("CREATE_ABOVE_LOCATION")]
        CreateAboveLocation,

        [IniEnum("CREATE_AT_LOCATION")]
        CreateAtLocation,
    }
}