// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Forms;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.JSInterop;
using Nito.AsyncEx;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Threading.Tasks;
using SLS4All.Compact.ComponentModel;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.Scripts;
using SLS4All.Compact.Slicing;
using static System.Runtime.InteropServices.JavaScript.JSType;
using SLS4All.Compact.Storage.PrintProfiles;
using SLS4All.Compact.Storage.PrinterSettings;
using SLS4All.Compact.Validation;
using SLS4All.Compact.Components;
using SLS4All.Compact.Configuration;
using SLS4All.Compact.Printing;
using SLS4All.Compact.Nesting;

namespace SLS4All.Compact.Pages
{
    public partial class SlicingPage : IAsyncDisposable
    {
        public class ValuesContainer
        {
            public decimal PrintableDepth = 200;
            public decimal LaserOn = 100;
            public decimal LaserOutlineSpeedXY = 2350;
            public decimal LaserFillSpeedXY = 2350;
            public decimal LaserOffSpeedA = 0;
            public decimal HotspotOverlapPercent = 25M;
            public decimal XCorrectionPercent = 100M;
            public decimal YCorrectionPercent = 100M;
            public int OutlineCount = 2;
            public int FillOutlineSkipCount = 1;
            public int IsFillEnabled = 1;
            public int IsBeginEndLayerEnabled = 0;
            public int IsBedPreparionEnabled = 0;
            public int IsPrintCapEnabled = 0;
            public decimal LayerThickness = 150;
            public decimal BeginLayerTemperatureTarget = 0;
            public decimal BedPreparationTemperatureTarget = 0;
            public decimal PrintCapTemperatureTarget = 0;
            public decimal BedPreparationHeight = 7000;
            public decimal PrintCapHeight = 7000;
            public decimal LaserFirstOutlineEnergyDensity = 0;
            public decimal LaserOtherOutlineEnergyDensity = 0;
            public decimal LaserFillEnergyDensity = 0;
            public decimal TemperatureDelay = 5;
            public decimal SinteredVolumeFactor = 2;
            public int DisableLayerAdditiveMovement = 0;
            public decimal OutlinePowerPrecision = 1;
            public decimal OutlinePowerIncrease = 0;
        }

        private Modal? _setupModal;
        private Modal? _profileModal;
        private int _layerIndex = 0;
        private int? _layerCount;
        private PrintProfile[] _profiles = [];

        public const string SelfPath = "/slicing";
        [Inject]
        public INestingService Nesting { get; set; } = default!;
        [Inject]
        public IPrintingService Printing { get; set; } = default!;
        [Inject]
        public ValuesContainer Values { get; set; } = default!;
        [Inject]
        public IToastProvider ToastProvider { get; set; } = default!;
        [Inject]
        public IPrintProfileStorage ProfileStorage { get; set; } = default!;
        [Inject]
        public IPrinterSettingsStorage SettingsStorage { get; set; } = default!;
        [Inject]
        public NavigationManager Navigation { get; set; } = default!;
        [Inject]
        public IValidationContextFactoryScoped ValidationContextFactory { get; set; } = default!;

        public int LayerIndex
        {
            get => _layerIndex;
            set
            {
                if (_layerIndex == value)
                    return;
                _layerIndex = value;
                StateHasChanged();
            }
        }

        public int? LayerCount
        {
            get => _layerCount;
            set
            {
                if (_layerCount == value)
                    return;
                _layerCount = value;
                StateHasChanged();
            }
        }

        protected override async Task OnInitializedAsync()
        {
            await base.OnInitializedAsync();
            Printing.BackgroundTask.StateChanged.AddHandler(TryInvokeStateHasChangedAsync);
            Printing.BackgroundTask.ExceptionHandler.AddHandler(OnException);
        }

        protected override void OnInitialized()
        {
            base.OnInitialized();
        }

        private Task OnException(Exception ex, CancellationToken cancel)
        {
            ToastProvider.Show(new ToastMessage
            {
                Type = ToastMessageType.Error,
                HeaderText = "Error during slicing",
                BodyText = ex.Message,
                Key = GetType(),
                Exception = ex,
            });
            return Task.CompletedTask;
        }

        private async Task<PrinterPowerSettings?> GetPrinterPowerSettings()
        {
            var context = ValidationContextFactory.CreateContext();
            var powerSettings = SettingsStorage.GetPowerSettingsDefaults();
            powerSettings.MergeFrom(SettingsStorage.GetPowerSettings());
            var powerSettingsValidation = await powerSettings.Validate(context);
            if (!powerSettingsValidation!.IsValid)
            {
                ToastProvider.Show(new ToastMessage
                {
                    HeaderText = "Invalid settings",
                    BodyText = "Printer power settings contain errors",
                    Type = ToastMessageType.Error,
                    Key = this,
                });
                return null;
            }
            return powerSettings;
        }

        private async Task<PrintSetup?> GetPrintingSetup()
        {
            var powerSettings = await GetPrinterPowerSettings();
            if (powerSettings == null)
                return null;
            return new PrintSetup
            {
                PrintableWidth = (decimal)Nesting.NestingDim.SizeX,
                PrintableHeight = (decimal)Nesting.NestingDim.SizeY,
                PrintableDepth = Values.PrintableDepth,
                LaserOnPercent = Values.LaserOn,
                LaserOutlineSpeedXY = Values.LaserOutlineSpeedXY,
                LaserFillSpeedXY = Values.LaserFillSpeedXY,
                LaserOffSpeedA = Values.LaserOffSpeedA,
                XProjectionPercent = Values.XCorrectionPercent,
                YProjectionPercent = Values.YCorrectionPercent,
                HotspotOverlapPercent = Values.HotspotOverlapPercent,
                OutlineCount = Values.OutlineCount,
                FillOutlineSkipCount = Values.FillOutlineSkipCount,
                IsFillEnabled = Values.IsFillEnabled != 0,
                IsBeginEndLayerEnabled = Values.IsBeginEndLayerEnabled != 0,
                IsBedPreparationEnabled = Values.IsBedPreparionEnabled != 0,
                IsPrintCapEnabled = Values.IsPrintCapEnabled != 0,
                LayerThickness = Values.LayerThickness,
                BedPreparationThickness = Values.BedPreparationHeight,
                PrintCapThickness = Values.PrintCapHeight,
                BeginLayerTemperatureTarget = Values.BeginLayerTemperatureTarget,
                BeginLayerTemperatureDelay = TimeSpan.FromSeconds((double)Values.TemperatureDelay),
                BedPreparationTemperatureTarget = Values.BedPreparationTemperatureTarget,
                BedPreparationTemperatureDelay = TimeSpan.FromSeconds((double)Values.TemperatureDelay),
                PrintCapTemperatureTarget = Values.PrintCapTemperatureTarget,
                PrintCapTemperatureDelay = TimeSpan.FromSeconds((double)Values.TemperatureDelay),
                LaserFirstOutlineEnergyDensity = Values.LaserFirstOutlineEnergyDensity,
                LaserOtherOutlineEnergyDensity = Values.LaserOtherOutlineEnergyDensity,
                LaserFillEnergyDensity = Values.LaserFillEnergyDensity,
                LaserWattage = powerSettings.LaserWattage!.Value,
                SinteredVolumeFactor = Values.SinteredVolumeFactor,
                DisableLayerAdditiveMovement = Values.DisableLayerAdditiveMovement != 0,
                OutlinePowerPrecision = Values.OutlinePowerPrecision,
                OutlinePowerIncrease = Values.OutlinePowerIncrease,
            };
        }

        public async Task DoPlot()
        {
            var layer = Printing.TryGetPreviewLayer(_layerIndex);
            if (layer != null)
            {
                var setup = await GetPrintingSetup();
                if (setup != null)
                {
                    await Printing.BackgroundTask.StartTask(new(), (cancel) => Printing.PlotLayer(Nesting, null, layer, setup));
                }
            }
        }

        private async Task<PrintSetup?> CreatePrintingSetup(PrintProfile? profile = null)
        {
            if (profile == null)
                return await GetPrintingSetup();
            else
            {
                var powerSettings = await GetPrinterPowerSettings();
                if (powerSettings == null)
                    return null;
                var setup = await Printing.CreateSetup(null, profile, powerSettings);
                return setup;
            }
        }

        public async Task DoSlice()
        {
            var setup = await CreatePrintingSetup(null);
            if (setup == null)
                return;
            await Printing.BackgroundTask.StartTask("slice", (cancel) =>
                Printing.ProcessPreviews(Nesting, null, setup, false, null, null, cancel));
        }

        public async Task DoPrint(PrintProfile? profile = null)
        {
            var setup = await CreatePrintingSetup(profile);
            if (setup == null)
                return;
            await Printing.BackgroundTask.StartValueTask(new(), (Func<CancellationToken, ValueTask>)(async (cancel) =>
            {
                Navigation.NavigateTo(PrinterStatus.SelfPath);
                await Task.Run(async () =>
                {
                    try
                    {
                        setup.LayerStart = _layerIndex;
                        setup.LayerCount = _layerCount ?? throw new ApplicationException("Layer count not set");
                        await Printing.PrintLayers(Nesting, null, null, setup, "testing job", null /* leave data in memory for user here */, null, null, cancel);
                        ToastProvider.Show(new ToastMessage
                        {
                            Type = ToastMessageType.Information,
                            HeaderText = "Printing completed",
                            BodyText = "Printing has completed successfully",
                            Key = GetType(),
                        });
                    }
                    catch (Exception ex)
                    {
                        ToastProvider.Show(new ToastMessage
                        {
                            Type = ToastMessageType.Error,
                            HeaderText = "Error during printing",
                            BodyText = ex.Message,
                            Key = GetType(),
                            Exception = ex,
                        });
                    }
                });
            }));
        }

        public async Task DoPrintProfiles(PrintProfile? profile = null)
        {
            if (profile == null)
            {
                var validationContext = ValidationContextFactory.CreateContext();

                var profiles = new List<PrintProfile>();
                foreach (var item in await ProfileStorage.GetOrderedMergedProfiles())
                {
                    var isValid = await item.Profile.Validate(validationContext);
                    if (isValid.IsValid)
                        profiles.Add(item.Profile);
                }
                _profiles = profiles.ToArray();
                await _profileModal!.Show();
            }
            else
            {
                await _profileModal!.Close();
                await DoPrint(profile);
            }
        }

        public void DoCancel()
        {
            Printing.BackgroundTask.Cancel();
        }

        public override async ValueTask DisposeAsync()
        {
            Nesting.BackgroundTask.StateChanged.RemoveHandler(TryInvokeStateHasChangedAsync);
            Printing.BackgroundTask.ExceptionHandler.RemoveHandler(OnException);
            await base.DisposeAsync();
        }
    }
}

