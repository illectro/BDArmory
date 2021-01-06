using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace BDArmory.Bullets
{
    public class BulletInfo
    {
        public string name { get; private set; }
        public float caliber { get; private set; }
        public float bulletMass { get; private set; }
        public float bulletVelocity { get; private set; }
        public bool explosive { get; private set; }
        public float tntMass { get; private set; }
        public string fuzeType { get; private set; }
        public int subProjectileCount { get; private set; }
        public float apBulletMod { get; private set; }
        public string bulletDragTypeName { get; private set; }
        public string projectileColor { get; private set; }
        public string startColor { get; private set; }
        public bool fadeColor { get; private set; }

        public static BulletInfos bullets;
        public static HashSet<string> bulletNames;
        public static BulletInfo defaultBullet;

        public BulletInfo(string name, float caliber, float bulletVelocity, float bulletMass,
                          bool explosive, float tntMass, string fuzeType, float apBulletDmg,
                          int subProjectileCount, string bulletDragTypeName, string projectileColor, string startColor, bool fadeColor)
        {
            this.name = name;
            this.caliber = caliber;
            this.bulletVelocity = bulletVelocity;
            this.bulletMass = bulletMass;
            this.explosive = explosive;
            this.tntMass = tntMass;
            this.fuzeType = fuzeType;
            this.apBulletMod = apBulletDmg;
            this.subProjectileCount = subProjectileCount;
            this.bulletDragTypeName = bulletDragTypeName;
            this.projectileColor = projectileColor;
            this.startColor = startColor;
            this.fadeColor = fadeColor;
        }

        public static void Load()
        {
            if (bullets != null) return; // Only load the bullet defs once on startup.
            bullets = new BulletInfos();
            if (bulletNames == null) bulletNames = new HashSet<string>();
            UrlDir.UrlConfig[] nodes = GameDatabase.Instance.GetConfigs("BULLET");
            ConfigNode node;

            // First locate BDA's default bullet definition so we can fill in missing fields.
            if (defaultBullet == null)
                for (int i = 0; i < nodes.Length; ++i)
                {
                    if (nodes[i].parent.name != "BD_Bullets") continue; // Ignore other config files.
                    node = nodes[i].config;
                    if (!node.HasValue("name") || (string)ParseField(nodes[i].config, "name", typeof(string)) != "def") continue; // Ignore other configs.
                    Debug.Log("[BDArmory]: Parsing default bullet definition from " + nodes[i].parent.name);
                    defaultBullet = new BulletInfo(
                        "def",
                        (float)ParseField(node, "caliber", typeof(float)),
                        (float)ParseField(node, "bulletVelocity", typeof(float)),
                        (float)ParseField(node, "bulletMass", typeof(float)),
                        (bool)ParseField(node, "explosive", typeof(bool)),
                        (float)ParseField(node, "tntMass", typeof(float)),
                        (string)ParseField(node, "fuzeType", typeof(string)),
                        (float)ParseField(node, "apBulletMod", typeof(float)),
                        (int)ParseField(node, "subProjectileCount", typeof(int)),
                        (string)ParseField(node, "bulletDragTypeName", typeof(string)),
                        (string)ParseField(node, "projectileColor", typeof(string)),
                        (string)ParseField(node, "startColor", typeof(string)),
                        (bool)ParseField(node, "fadeColor", typeof(bool))
                    );
                    bullets.Add(defaultBullet);
                    bulletNames.Add("def");
                    break;
                }
            if (defaultBullet == null) throw new ArgumentException("Failed to find BDArmory's default bullet definition.", "defaultBullet");

            // Now add in the rest of the bullets.
            for (int i = 0; i < nodes.Length; i++)
            {
                string name_ = "";
                try
                {
                    node = nodes[i].config;
                    name_ = (string)ParseField(node, "name", typeof(string));
                    if (bulletNames.Contains(name_)) // Avoid duplicates.
                    {
                        if (nodes[i].parent.name != "BD_Bullets" || name_ != "def") // Don't report the default bullet definition as a duplicate.
                            Debug.LogError("[BDArmory]: Bullet definition " + name_ + " from " + nodes[i].parent.name + " already exists, skipping.");
                        continue;
                    }
                    Debug.Log("[BDArmory]: Parsing definition of bullet " + name_ + " from " + nodes[i].parent.name);
                    bullets.Add(
                        new BulletInfo(
                            name_,
                            (float)ParseField(node, "caliber", typeof(float)),
                            (float)ParseField(node, "bulletVelocity", typeof(float)),
                            (float)ParseField(node, "bulletMass", typeof(float)),
                            (bool)ParseField(node, "explosive", typeof(bool)),
                            (float)ParseField(node, "tntMass", typeof(float)),
                            (string)ParseField(node, "fuzeType", typeof(string)),
                            (float)ParseField(node, "apBulletMod", typeof(float)),
                            (int)ParseField(node, "subProjectileCount", typeof(int)),
                            (string)ParseField(node, "bulletDragTypeName", typeof(string)),
                            (string)ParseField(node, "projectileColor", typeof(string)),
                            (string)ParseField(node, "startColor", typeof(string)),
                            (bool)ParseField(node, "fadeColor", typeof(bool))
                        )
                    );
                    bulletNames.Add(name_);
                }
                catch (Exception e)
                {
                    Debug.LogError("[BDArmory]: Error Loading Bullet Config '" + name_ + "' | " + e.ToString());
                }
            }
        }

        private static object ParseField(ConfigNode node, string field, Type type)
        {
            try
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
            catch (Exception e)
            {
                if (defaultBullet != null)
                {
                    // Give a warning about the missing or invalid value, then use the default value using reflection to find the field.
                    var defaultValue = typeof(BulletInfo).GetProperty(field, BindingFlags.Public | BindingFlags.Instance).GetValue(defaultBullet);
                    Debug.LogError("[BDArmory]: Using default value of " + defaultValue.ToString() + " for " + field + " | " + e.ToString());
                    return defaultValue;
                }
                else
                    throw;
            }
        }
    }

    public class BulletInfos : List<BulletInfo>
    {
        public BulletInfo this[string name]
        {
            get { return Find((value) => { return value.name == name; }); }
        }
    }
}
