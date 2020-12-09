using System.Collections.Generic;
using UnityEngine;

namespace BDArmory.Core
{
    [KSPAddon(KSPAddon.Startup.Flight, false)]
    public class PartExploderSystem : MonoBehaviour
    {
        private static readonly Queue<Part> ExplodingPartsQueue = new Queue<Part>();

        public static void AddPartToExplode(Part p)
        {
            if (p != null && !ExplodingPartsQueue.Contains(p))
            {
                ExplodingPartsQueue.Enqueue(p);
            }
        }

        private void OnDestroy()
        {
            ExplodingPartsQueue.Clear();
        }

        public void Update()
        {
            if (ExplodingPartsQueue.Count == 0) return;

            do
            {
                Part part = ExplodingPartsQueue.Dequeue();

                if (part != null)
                {
                    part.explode();
                    // part.Die(); // DEBUG check whether part.explode() actually removes the part.
                }
            } while (ExplodingPartsQueue.Count > 0);
        }
    }
}
