using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Pages.Wizards
{
    public class GalvoCalibrationWizardOptions
    {
        public double Z2MoveDepth { get; set; } = 3500;
        public decimal CalibrationRadius { get; set; } = 435;
        public int CalibrationSteps { get; set; } = 42;
        public decimal CalibrationDensityStep { get; set; } = 5;
        public decimal CalibrationMargin { get; set; } = 5;
        public TimeSpan Dwell { get; set; } = TimeSpan.Zero;
        public int Steps { get; set; } = 360;
        public decimal DefaultSpeedA { get; set; } = 75000;
        public decimal LaserOnPrecent { get; set; } = 100;
        public decimal DefaultSpeedARelativeLaserWattage { get; set; } = 10000;
        public decimal ScalingRadius { get; set; } = 80;
        public decimal ScalingStep { get; set; } = 5;
        public decimal Precision { get; set; } = 1;
    }
}
