using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoLoot
{
    public class ModAPI : IModApi
    {
        public void InitMod(Mod _modInstance)
        {
            ModEvents.GameStartDone.RegisterHandler(GameStartDone);
        }

        private void GameStartDone()
        {
            PatchHelper.DoPatches();
        }
    }
}
