using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Pages.Wizards
{
    public class OpticalSetupWizardOptions
    {
        public double LaserMinimumVisibleWattageLow { get; set; } = 600;
        public double LaserMinimumVisibleWattageHigh { get; set; } = 900;
        public double GalvoHolderLaserX { get; set; } = 500;
        public double GalvoHolderLaserY { get; set; } = 900;
        public double Z2FocusDepth { get; set; } = 3500;
    }
}
