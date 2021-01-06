using System;
using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class RocketInfo
    {
        public string name { get; private set; }
        public float rocketMass { get; private set; }
        public float caliber { get; private set; }
        public float thrust { get; private set; }
        public float thrustTime { get; private set; }
        public bool shaped { get; private set; }
        public bool flak { get; private set; }
        public bool explosive { get; private set; }
        public float tntMass { get; private set; }
        public int subProjectileCount { get; private set; }
        public float thrustDeviation { get; private set; }
        public string rocketModelPath { get; private set; }

        public static RocketInfos rockets;

        public RocketInfo(string name, float rocketMass, float caliber, float thrust, float thrustTime,
                          bool shaped, bool flak, bool explosive, float tntMass, int subProjectileCount, float thrustDeviation, string rocketModelPath)

        {
            this.name = name;
            this.rocketMass = rocketMass;
            this.caliber = caliber;
            this.thrust = thrust;
            this.thrustTime = thrustTime;
            this.shaped = shaped;
            this.flak = flak;
            this.explosive = explosive;
            this.tntMass = tntMass;
            this.subProjectileCount = subProjectileCount;
            this.thrustDeviation = thrustDeviation;
            this.rocketModelPath = rocketModelPath;
        }

        public static void Load()
        {
            if (rockets != null) return; // Only load them once on startup.
            rockets = new RocketInfos();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("ROCKET");
            for (int i = 0; i < nodes.Length; i++)
            {
                string name_ = "";
                try
                {
                    ConfigNode node = nodes[i].config;
                    name_ = (string)ParseField(node, "name", typeof(string));
                    rockets.Add(
                        new RocketInfo(
                            name_,
                            (float)ParseField(node, "rocketMass", typeof(float)),
                            (float)ParseField(node, "caliber", typeof(float)),
                            (float)ParseField(node, "thrust", typeof(float)),
                            (float)ParseField(node, "thrustTime", typeof(float)),
                            (bool)ParseField(node, "shaped", typeof(bool)),
                            (bool)ParseField(node, "flak", typeof(bool)),
                            (bool)ParseField(node, "explosive", typeof(bool)),
                            (float)ParseField(node, "tntMass", typeof(float)),
                            (int)ParseField(node, "subProjectileCount", typeof(int)),
                            (float)ParseField(node, "thrustDeviation", typeof(float)),
                            (string)ParseField(node, "rocketModelPath", typeof(string))
                        )
                    );
                }
                catch (Exception e)
                {
                    Debug.LogError("[BDArmory]: Error Loading Rocket Config '" + name_ + "' | " + e.ToString());
                }
            }
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            if (!node.HasValue(field))
                throw new ArgumentNullException(field, "Field '" + field + "' is missing.");
            var value = node.GetValue(field);
            try
            {
                if (type == typeof(string))
                { return value; }
                else if (type == typeof(bool))
                { return bool.Parse(value); }
                else if (type == typeof(int))
                { return int.Parse(value); }
                else if (type == typeof(float))
                { return float.Parse(value); }
                else
                { throw new ArgumentException("Invalid type specified."); }
            }
            catch (Exception e)
            { throw new ArgumentException("Field '" + field + "': '" + value + "' could not be parsed as '" + type.ToString() + "' | " + e.ToString(), field); }
        }
    }

    public class RocketInfos : List<RocketInfo>
    {
        public RocketInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}
