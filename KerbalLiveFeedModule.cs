using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;

namespace KerbalLiveFeed
{
    public class KerbalLiveFeedModule : PartModule
    {

        public KLFVessel klfVessel = null;

        public override void OnStart(StartState state)
        {
            Debug.Log("*** KLF version "+KLFCommon.PROGRAM_VERSION+" started");
        }

        public override void OnUpdate()
        {
            KLFManager.Instance.updateStep();
        }
    }
}