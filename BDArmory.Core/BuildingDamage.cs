using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Core
{
    [KSPAddon(KSPAddon.Startup.MainMenu, false)]
    public class BuildingDamage : ScenarioDestructibles
    {
        public override void OnAwake()
        {
            Debug.Log("[BDArmory]: Modifying Buildings");

            foreach (KeyValuePair<string, ProtoDestructible> bldg in protoDestructibles)
            {
                using (var building = bldg.Value.dBuildingRefs.GetEnumerator())
                    while( building.MoveNext())
                    {
                        building.Current.damageDecay = 600f;
                        building.Current.impactMomentumThreshold *= 150;
                    }
            }
        }
    }
}
