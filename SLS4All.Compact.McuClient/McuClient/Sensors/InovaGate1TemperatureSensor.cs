// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

﻿using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using SLS4All.Compact.Collections;
using SLS4All.Compact.Diagnostics;
using SLS4All.Compact.Helpers;
using SLS4All.Compact.McuClient.Messages;
using SLS4All.Compact.McuClient.Pins;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.McuClient.Sensors
{
    public class InovaGate1TemperatureSensorOptions
    {
        public required string Bus { get; set; }
        public required string CsPin { get; set; }
        public required int Channel { get; set; }
        public required float MinTemperature { get; set; }
        public required float MaxTemperature { get; set; }
        public int SpiSpeed { get; set; } = 15_000_000;
        public TimeSpan ReadPeriod { get; set; } = TimeSpan.FromSeconds(0.3);
        public TimeSpan AverageDuration { get; set; } = TimeSpan.FromSeconds(0);
    }

    public sealed class InovaGate1TemperatureSensor : IMcuTemperatureSensor
    {
        private readonly static float[] s_tempTable = [-30f, -29.75f, -29.5f, -29.25f, -29f, -28.75f, -28.5f, -28.25f, -28f, -27.75f, -27.5f, -27.25f, -27f, -26.75f, -26.5f, -26.25f, -26f, -25.75f, -25.5f, -25.25f, -25f, -24.75f, -24.5f, -24.25f, -24f, -23.75f, -23.5f, -23.25f, -23f, -22.75f, -22.5f, -22.25f, -22f, -21.75f, -21.5f, -21.25f, -21f, -20.75f, -20.5f, -20.25f, -20f, -19.75f, -19.5f, -19.25f, -19f, -18.75f, -18.5f, -18.25f, -18f, -17.75f, -17.5f, -17.25f, -17f, -16.75f, -16.5f, -16.25f, -16f, -15.75f, -15.5f, -15.25f, -15f, -14.75f, -14.5f, -14.25f, -14f, -13.75f, -13.5f, -13.25f, -13f, -12.75f, -12.5f, -12.25f, -12f, -11.75f, -11.5f, -11.25f, -11f, -10.75f, -10.5f, -10.25f, -10f, -9.75f, -9.5f, -9.25f, -9f, -8.75f, -8.5f, -8.25f, -8f, -7.75f, -7.5f, -7.25f, -7f, -6.75f, -6.5f, -6.25f, -6f, -5.75f, -5.5f, -5.25f, -5f, -4.75f, -4.5f, -4.25f, -4f, -3.75f, -3.5f, -3.25f, -3f, -2.75f, -2.5f, -2.25f, -2f, -1.75f, -1.5f, -1.25f, -1f, -0.75f, -0.5f, -0.25f, 0f, 0.25f, 0.5f, 0.75f, 1f, 1.25f, 1.5f, 1.75f, 2f, 2.25f, 2.5f, 2.75f, 3f, 3.25f, 3.5f, 3.75f, 4f, 4.25f, 4.5f, 4.75f, 5f, 5.25f, 5.5f, 5.75f, 6f, 6.25f, 6.5f, 6.75f, 7f, 7.25f, 7.5f, 7.75f, 8f, 8.25f, 8.5f, 8.75f, 9f, 9.25f, 9.5f, 9.75f, 10f, 10.25f, 10.5f, 10.75f, 11f, 11.25f, 11.5f, 11.75f, 12f, 12.25f, 12.5f, 12.75f, 13f, 13.25f, 13.5f, 13.75f, 14f, 14.25f, 14.5f, 14.75f, 15f, 15.25f, 15.5f, 15.75f, 16f, 16.25f, 16.5f, 16.75f, 17f, 17.25f, 17.5f, 17.75f, 18f, 18.25f, 18.5f, 18.75f, 19f, 19.25f, 19.5f, 19.75f, 20f, 20.25f, 20.5f, 20.75f, 21f, 21.25f, 21.5f, 21.75f, 22f, 22.25f, 22.5f, 22.75f, 23f, 23.25f, 23.5f, 23.75f, 24f, 24.25f, 24.5f, 24.75f, 25f, 25.25f, 25.5f, 25.75f, 26f, 26.25f, 26.5f, 26.75f, 27f, 27.25f, 27.5f, 27.75f, 28f, 28.25f, 28.5f, 28.75f, 29f, 29.25f, 29.5f, 29.75f, 30f, 30.25f, 30.5f, 30.75f, 31f, 31.25f, 31.5f, 31.75f, 32f, 32.25f, 32.5f, 32.75f, 33f, 33.25f, 33.5f, 33.75f, 34f, 34.25f, 34.5f, 34.75f, 35f, 35.25f, 35.5f, 35.75f, 36f, 36.25f, 36.5f, 36.75f, 37f, 37.25f, 37.5f, 37.75f, 38f, 38.25f, 38.5f, 38.75f, 39f, 39.25f, 39.5f, 39.75f, 40f, 40.25f, 40.5f, 40.75f, 41f, 41.25f, 41.5f, 41.75f, 42f, 42.25f, 42.5f, 42.75f, 43f, 43.25f, 43.5f, 43.75f, 44f, 44.25f, 44.5f, 44.75f, 45f, 45.25f, 45.5f, 45.75f, 46f, 46.25f, 46.5f, 46.75f, 47f, 47.25f, 47.5f, 47.75f, 48f, 48.25f, 48.5f, 48.75f, 49f, 49.25f, 49.5f, 49.75f, 50f, 50.25f, 50.5f, 50.75f, 51f, 51.25f, 51.5f, 51.75f, 52f, 52.25f, 52.5f, 52.75f, 53f, 53.25f, 53.5f, 53.75f, 54f, 54.25f, 54.5f, 54.75f, 55f, 55.25f, 55.5f, 55.75f, 56f, 56.25f, 56.5f, 56.75f, 57f, 57.25f, 57.5f, 57.75f, 58f, 58.25f, 58.5f, 58.75f, 59f, 59.25f, 59.5f, 59.75f, 60f, 60.25f, 60.5f, 60.75f, 61f, 61.25f, 61.5f, 61.75f, 62f, 62.25f, 62.5f, 62.75f, 63f, 63.25f, 63.5f, 63.75f, 64f, 64.25f, 64.5f, 64.75f, 65f, 65.25f, 65.5f, 65.75f, 66f, 66.25f, 66.5f, 66.75f, 67f, 67.25f, 67.5f, 67.75f, 68f, 68.25f, 68.5f, 68.75f, 69f, 69.25f, 69.5f, 69.75f, 70f, 70.25f, 70.5f, 70.75f, 71f, 71.25f, 71.5f, 71.75f, 72f, 72.25f, 72.5f, 72.75f, 73f, 73.25f, 73.5f, 73.75f, 74f, 74.25f, 74.5f, 74.75f, 75f, 75.25f, 75.5f, 75.75f, 76f, 76.25f, 76.5f, 76.75f, 77f, 77.25f, 77.5f, 77.75f, 78f, 78.25f, 78.5f, 78.75f, 79f, 79.25f, 79.5f, 79.75f, 80f, 80.25f, 80.5f, 80.75f, 81f, 81.25f, 81.5f, 81.75f, 82f, 82.25f, 82.5f, 82.75f, 83f, 83.25f, 83.5f, 83.75f, 84f, 84.25f, 84.5f, 84.75f, 85f, 85.25f, 85.5f, 85.75f, 86f, 86.25f, 86.5f, 86.75f, 87f, 87.25f, 87.5f, 87.75f, 88f, 88.25f, 88.5f, 88.75f, 89f, 89.25f, 89.5f, 89.75f, 90f, 90.25f, 90.5f, 90.75f, 91f, 91.25f, 91.5f, 91.75f, 92f, 92.25f, 92.5f, 92.75f, 93f, 93.25f, 93.5f, 93.75f, 94f, 94.25f, 94.5f, 94.75f, 95f, 95.25f, 95.5f, 95.75f, 96f, 96.25f, 96.5f, 96.75f, 97f, 97.25f, 97.5f, 97.75f, 98f, 98.25f, 98.5f, 98.75f, 99f, 99.25f, 99.5f, 99.75f, 100f, 100.25f, 100.5f, 100.75f, 101f, 101.25f, 101.5f, 101.75f, 102f, 102.25f, 102.5f, 102.75f, 103f, 103.25f, 103.5f, 103.75f, 104f, 104.25f, 104.5f, 104.75f, 105f, 105.25f, 105.5f, 105.75f, 106f, 106.25f, 106.5f, 106.75f, 107f, 107.25f, 107.5f, 107.75f, 108f, 108.25f, 108.5f, 108.75f, 109f, 109.25f, 109.5f, 109.75f, 110f, 110.25f, 110.5f, 110.75f, 111f, 111.25f, 111.5f, 111.75f, 112f, 112.25f, 112.5f, 112.75f, 113f, 113.25f, 113.5f, 113.75f, 114f, 114.25f, 114.5f, 114.75f, 115f, 115.25f, 115.5f, 115.75f, 116f, 116.25f, 116.5f, 116.75f, 117f, 117.25f, 117.5f, 117.75f, 118f, 118.25f, 118.5f, 118.75f, 119f, 119.25f, 119.5f, 119.75f, 120f, 120.25f, 120.5f, 120.75f, 121f, 121.25f, 121.5f, 121.75f, 122f, 122.25f, 122.5f, 122.75f, 123f, 123.25f, 123.5f, 123.75f, 124f, 124.25f, 124.5f, 124.75f, 125f, 125.25f, 125.5f, 125.75f, 126f, 126.25f, 126.5f, 126.75f, 127f, 127.25f, 127.5f, 127.75f, 128f, 128.25f, 128.5f, 128.75f, 129f, 129.25f, 129.5f, 129.75f, 130f, 130.25f, 130.5f, 130.75f, 131f, 131.25f, 131.5f, 131.75f, 132f, 132.25f, 132.5f, 132.75f, 133f, 133.25f, 133.5f, 133.75f, 134f, 134.25f, 134.5f, 134.75f, 135f, 135.25f, 135.5f, 135.75f, 136f, 136.25f, 136.5f, 136.75f, 137f, 137.25f, 137.5f, 137.75f, 138f, 138.25f, 138.5f, 138.75f, 139f, 139.25f, 139.5f, 139.75f, 140f, 140.25f, 140.5f, 140.75f, 141f, 141.25f, 141.5f, 141.75f, 142f, 142.25f, 142.5f, 142.75f, 143f, 143.25f, 143.5f, 143.75f, 144f, 144.25f, 144.5f, 144.75f, 145f, 145.25f, 145.5f, 145.75f, 146f, 146.25f, 146.5f, 146.75f, 147f, 147.25f, 147.5f, 147.75f, 148f, 148.25f, 148.5f, 148.75f, 149f, 149.25f, 149.5f, 149.75f, 150f, 150.25f, 150.5f, 150.75f, 151f, 151.25f, 151.5f, 151.75f, 152f, 152.25f, 152.5f, 152.75f, 153f, 153.25f, 153.5f, 153.75f, 154f, 154.25f, 154.5f, 154.75f, 155f, 155.25f, 155.5f, 155.75f, 156f, 156.25f, 156.5f, 156.75f, 157f, 157.25f, 157.5f, 157.75f, 158f, 158.25f, 158.5f, 158.75f, 159f, 159.25f, 159.5f, 159.75f, 160f, 160.25f, 160.5f, 160.75f, 161f, 161.25f, 161.5f, 161.75f, 162f, 162.25f, 162.5f, 162.75f, 163f, 163.25f, 163.5f, 163.75f, 164f, 164.25f, 164.5f, 164.75f, 165f, 165.25f, 165.5f, 165.75f, 166f, 166.25f, 166.5f, 166.75f, 167f, 167.25f, 167.5f, 167.75f, 168f, 168.25f, 168.5f, 168.75f, 169f, 169.25f, 169.5f, 169.75f, 170f, 170.25f, 170.5f, 170.75f, 171f, 171.25f, 171.5f, 171.75f, 172f, 172.25f, 172.5f, 172.75f, 173f, 173.25f, 173.5f, 173.75f, 174f, 174.25f, 174.5f, 174.75f, 175f, 175.25f, 175.5f, 175.75f, 176f, 176.25f, 176.5f, 176.75f, 177f, 177.25f, 177.5f, 177.75f, 178f, 178.25f, 178.5f, 178.75f, 179f, 179.25f, 179.5f, 179.75f, 180f, 180.25f, 180.5f, 180.75f, 181f, 181.25f, 181.5f, 181.75f, 182f, 182.25f, 182.5f, 182.75f, 183f, 183.25f, 183.5f, 183.75f, 184f, 184.25f, 184.5f, 184.75f, 185f, 185.25f, 185.5f, 185.75f, 186f, 186.25f, 186.5f, 186.75f, 187f, 187.25f, 187.5f, 187.75f, 188f, 188.25f, 188.5f, 188.75f, 189f, 189.25f, 189.5f, 189.75f, 190f, 190.25f, 190.5f, 190.75f, 191f, 191.25f, 191.5f, 191.75f, 192f, 192.25f, 192.5f, 192.75f, 193f, 193.25f, 193.5f, 193.75f, 194f, 194.25f, 194.5f, 194.75f, 195f, 195.25f, 195.5f, 195.75f, 196f, 196.25f, 196.5f, 196.75f, 197f, 197.25f, 197.5f, 197.75f, 198f, 198.25f, 198.5f, 198.75f, 199f, 199.25f, 199.5f, 199.75f, 200f, 200.25f, 200.5f, 200.75f, 201f, 201.25f, 201.5f, 201.75f, 202f, 202.25f, 202.5f, 202.75f, 203f, 203.25f, 203.5f, 203.75f, 204f, 204.25f, 204.5f, 204.75f, 205f, 205.25f, 205.5f, 205.75f, 206f, 206.25f, 206.5f, 206.75f, 207f, 207.25f, 207.5f, 207.75f, 208f, 208.25f, 208.5f, 208.75f, 209f, 209.25f, 209.5f, 209.75f, 210f, 210.25f, 210.5f, 210.75f, 211f, 211.25f, 211.5f, 211.75f, 212f, 212.25f, 212.5f, 212.75f, 213f, 213.25f, 213.5f, 213.75f, 214f, 214.25f, 214.5f, 214.75f, 215f, 215.25f, 215.5f, 215.75f, 216f, 216.25f, 216.5f, 216.75f, 217f, 217.25f, 217.5f, 217.75f, 218f, 218.25f, 218.5f, 218.75f, 219f, 219.25f, 219.5f, 219.75f, 220f, 220.25f, 220.5f, 220.75f, 221f, 221.25f, 221.5f, 221.75f, 222f, 222.25f, 222.5f, 222.75f, 223f, 223.25f, 223.5f, 223.75f, 224f, 224.25f, 224.5f, 224.75f, 225f, 225.25f, 225.5f, 225.75f, 226f, 226.25f, 226.5f, 226.75f, 227f, 227.25f, 227.5f, 227.75f, 228f, 228.25f, 228.5f, 228.75f, 229f, 229.25f, 229.5f, 229.75f, 230f, 230.25f, 230.5f, 230.75f, 231f, 231.25f, 231.5f, 231.75f, 232f, 232.25f, 232.5f, 232.75f, 233f, 233.25f, 233.5f, 233.75f, 234f, 234.25f, 234.5f, 234.75f, 235f, 235.25f, 235.5f, 235.75f, 236f, 236.25f, 236.5f, 236.75f, 237f, 237.25f, 237.5f, 237.75f, 238f, 238.25f, 238.5f, 238.75f, 239f, 239.25f, 239.5f, 239.75f, 240f, 240.25f, 240.5f, 240.75f, 241f, 241.25f, 241.5f, 241.75f, 242f, 242.25f, 242.5f, 242.75f, 243f, 243.25f, 243.5f, 243.75f, 244f, 244.25f, 244.5f, 244.75f, 245f, 245.25f, 245.5f, 245.75f, 246f, 246.25f, 246.5f, 246.75f, 247f, 247.25f, 247.5f, 247.75f, 248f, 248.25f, 248.5f, 248.75f, 249f, 249.25f, 249.5f, 249.75f, 250f, 250.25f, 250.5f, 250.75f, 251f, 251.25f, 251.5f, 251.75f, 252f, 252.25f, 252.5f, 252.75f, 253f, 253.25f, 253.5f, 253.75f, 254f, 254.25f, 254.5f, 254.75f, 255f, 255.25f, 255.5f, 255.75f, 256f, 256.25f, 256.5f, 256.75f, 257f, 257.25f, 257.5f, 257.75f, 258f, 258.25f, 258.5f, 258.75f, 259f, 259.25f, 259.5f, 259.75f, 260f, 260.25f, 260.5f, 260.75f, 261f, 261.25f, 261.5f, 261.75f, 262f, 262.25f, 262.5f, 262.75f, 263f, 263.25f, 263.5f, 263.75f, 264f, 264.25f, 264.5f, 264.75f, 265f, 265.25f, 265.5f, 265.75f, 266f, 266.25f, 266.5f, 266.75f, 267f, 267.25f, 267.5f, 267.75f, 268f, 268.25f, 268.5f, 268.75f, 269f, 269.25f, 269.5f, 269.75f, 270f, 270.25f, 270.5f, 270.75f, 271f, 271.25f, 271.5f, 271.75f, 272f, 272.25f, 272.5f, 272.75f, 273f, 273.25f, 273.5f, 273.75f, 274f, 274.25f, 274.5f, 274.75f, 275f, 275.25f, 275.5f, 275.75f, 276f, 276.25f, 276.5f, 276.75f, 277f, 277.25f, 277.5f, 277.75f, 278f, 278.25f, 278.5f, 278.75f, 279f, 279.25f, 279.5f, 279.75f, 280f, 280.25f, 280.5f, 280.75f, 281f, 281.25f, 281.5f, 281.75f, 282f, 282.25f, 282.5f, 282.75f, 283f, 283.25f, 283.5f, 283.75f, 284f, 284.25f, 284.5f, 284.75f, 285f, 285.25f, 285.5f, 285.75f, 286f, 286.25f, 286.5f, 286.75f, 287f, 287.25f, 287.5f, 287.75f, 288f, 288.25f, 288.5f, 288.75f, 289f, 289.25f, 289.5f, 289.75f, 290f, 290.25f, 290.5f, 290.75f, 291f, 291.25f, 291.5f, 291.75f, 292f, 292.25f, 292.5f, 292.75f, 293f, 293.25f, 293.5f, 293.75f, 294f, 294.25f, 294.5f, 294.75f, 295f, 295.25f, 295.5f, 295.75f, 296f, 296.25f, 296.5f, 296.75f, 297f, 297.25f, 297.5f, 297.75f, 298f, 298.25f, 298.5f, 298.75f, 299f, 299.25f, 299.5f, 299.75f, 300f,];
        private readonly static float[] s_resTable = [4083.925f, 4083.759f, 4083.588f, 4083.411f, 4083.229f, 4083.054f, 4082.873f, 4082.687f, 4082.496f, 4082.311f, 4082.121f, 4081.925f, 4081.723f, 4081.528f, 4081.328f, 4081.122f, 4080.909f, 4080.705f, 4080.494f, 4080.277f, 4080.054f, 4079.837f, 4079.613f, 4079.383f, 4079.147f, 4078.918f, 4078.684f, 4078.442f, 4078.193f, 4077.953f, 4077.706f, 4077.452f, 4077.190f, 4076.938f, 4076.678f, 4076.411f, 4076.136f, 4075.871f, 4075.599f, 4075.318f, 4075.030f, 4074.751f, 4074.465f, 4074.170f, 4073.867f, 4073.574f, 4073.273f, 4072.964f, 4072.646f, 4072.339f, 4072.024f, 4071.699f, 4071.366f, 4071.044f, 4070.713f, 4070.373f, 4070.024f, 4069.686f, 4069.339f, 4068.983f, 4068.617f, 4068.258f, 4067.889f, 4067.510f, 4067.121f, 4066.745f, 4066.358f, 4065.961f, 4065.553f, 4065.159f, 4064.754f, 4064.338f, 4063.910f, 4063.498f, 4063.074f, 4062.638f, 4062.191f, 4061.759f, 4061.315f, 4060.859f, 4060.391f, 4059.938f, 4059.473f, 4058.996f, 4058.506f, 4058.033f, 4057.547f, 4057.048f, 4056.535f, 4056.040f, 4055.532f, 4055.010f, 4054.475f, 4053.957f, 4053.426f, 4052.881f, 4052.322f, 4051.781f, 4051.227f, 4050.658f, 4050.074f, 4049.499f, 4048.909f, 4048.304f, 4047.683f, 4047.082f, 4046.466f, 4045.834f, 4045.186f, 4044.559f, 4043.915f, 4043.256f, 4042.578f, 4041.924f, 4041.252f, 4040.564f, 4039.857f, 4039.174f, 4038.473f, 4037.755f, 4037.018f, 4036.302f, 4035.568f, 4034.815f, 4034.043f, 4033.296f, 4032.531f, 4031.746f, 4030.941f, 4030.162f, 4029.365f, 4028.547f, 4027.708f, 4026.897f, 4026.065f, 4025.213f, 4024.340f, 4023.495f, 4022.629f, 4021.742f, 4020.832f, 4019.949f, 4019.044f, 4018.118f, 4017.168f, 4016.248f, 4015.307f, 4014.342f, 4013.354f, 4012.397f, 4011.418f, 4010.415f, 4009.387f, 4008.391f, 4007.373f, 4006.329f, 4005.261f, 4004.226f, 4003.167f, 4002.083f, 4000.972f, 3999.877f, 3998.755f, 3997.606f, 3996.430f, 3995.291f, 3994.125f, 3992.931f, 3991.709f, 3990.524f, 3989.312f, 3988.072f, 3986.802f, 3985.571f, 3984.312f, 3983.024f, 3981.706f, 3980.427f, 3979.120f, 3977.783f, 3976.414f, 3975.089f, 3973.734f, 3972.348f, 3970.929f, 3969.554f, 3968.148f, 3966.710f, 3965.240f, 3963.813f, 3962.355f, 3960.864f, 3959.340f, 3957.861f, 3956.349f, 3954.804f, 3953.224f, 3951.691f, 3950.125f, 3948.524f, 3946.887f, 3945.278f, 3943.633f, 3941.952f, 3940.233f, 3938.566f, 3936.862f, 3935.121f, 3933.341f, 3931.614f, 3929.849f, 3928.046f, 3926.204f, 3924.416f, 3922.589f, 3920.724f, 3918.817f, 3916.967f, 3915.077f, 3913.147f, 3911.175f, 3909.321f, 3907.430f, 3905.499f, 3903.529f, 3901.615f, 3899.662f, 3897.669f, 3895.635f, 3893.659f, 3891.644f, 3889.588f, 3887.489f, 3885.450f, 3883.371f, 3881.250f, 3879.086f, 3876.983f, 3874.838f, 3872.651f, 3870.420f, 3868.186f, 3865.907f, 3863.582f, 3861.210f, 3858.905f, 3856.554f, 3854.156f, 3851.709f, 3849.332f, 3846.908f, 3844.436f, 3841.914f, 3839.463f, 3836.965f, 3834.417f, 3831.818f, 3829.293f, 3826.719f, 3824.094f, 3821.418f, 3818.801f, 3816.134f, 3813.414f, 3810.641f, 3807.946f, 3805.199f, 3802.400f, 3799.545f, 3796.771f, 3793.944f, 3791.063f, 3788.127f, 3785.272f, 3782.364f, 3779.401f, 3776.381f, 3773.445f, 3770.455f, 3767.408f, 3764.304f, 3761.252f, 3758.142f, 3754.974f, 3751.746f, 3748.609f, 3745.413f, 3742.158f, 3738.841f, 3735.617f, 3732.334f, 3728.990f, 3725.584f, 3722.273f, 3718.901f, 3715.468f, 3711.972f, 3708.572f, 3705.112f, 3701.589f, 3698.001f, 3694.439f, 3690.812f, 3687.120f, 3683.359f, 3679.703f, 3675.982f, 3672.193f, 3668.335f, 3664.585f, 3660.768f, 3656.882f, 3652.927f, 3649.081f, 3645.168f, 3641.185f, 3637.132f, 3633.190f, 3629.180f, 3625.100f, 3620.947f, 3617.169f, 3613.331f, 3609.431f, 3605.466f, 3601.606f, 3597.684f, 3593.699f, 3589.650f, 3585.707f, 3581.701f, 3577.632f, 3573.498f, 3569.471f, 3565.382f, 3561.229f, 3557.010f, 3552.900f, 3548.727f, 3544.490f, 3540.186f, 3535.815f, 3531.374f, 3526.862f, 3522.278f, 3517.815f, 3513.283f, 3508.678f, 3504.000f, 3499.447f, 3494.822f, 3490.126f, 3485.355f, 3480.711f, 3475.995f, 3471.207f, 3466.344f, 3461.610f, 3456.804f, 3451.924f, 3446.970f, 3442.078f, 3437.111f, 3432.069f, 3426.948f, 3421.964f, 3416.905f, 3411.770f, 3406.555f, 3401.480f, 3396.329f, 3391.101f, 3385.794f, 3380.628f, 3375.386f, 3370.067f, 3364.668f, 3359.413f, 3354.082f, 3348.672f, 3343.184f, 3337.816f, 3332.371f, 3326.848f, 3321.243f, 3315.787f, 3310.253f, 3304.640f, 3298.947f, 3293.404f, 3287.784f, 3282.084f, 3276.303f, 3270.674f, 3264.968f, 3259.181f, 3253.314f, 3247.601f, 3241.811f, 3235.940f, 3229.989f, 3224.077f, 3218.084f, 3212.008f, 3205.848f, 3199.851f, 3193.774f, 3187.613f, 3181.367f, 3175.288f, 3169.127f, 3162.883f, 3156.554f, 3150.393f, 3144.152f, 3137.827f, 3131.418f, 3125.179f, 3118.860f, 3112.458f, 3105.971f, 3099.851f, 3093.655f, 3087.381f, 3081.028f, 3074.838f, 3068.572f, 3062.228f, 3055.806f, 3049.551f, 3043.221f, 3036.814f, 3030.328f, 3024.008f, 3017.613f, 3011.140f, 3004.590f, 2998.210f, 2991.755f, 2985.223f, 2978.614f, 2972.014f, 2965.335f, 2958.576f, 2951.736f, 2945.076f, 2938.339f, 2931.521f, 2924.623f, 2917.907f, 2911.114f, 2904.241f, 2897.289f, 2890.517f, 2883.667f, 2876.740f, 2869.733f, 2862.909f, 2856.008f, 2849.030f, 2841.973f, 2835.089f, 2828.129f, 2821.091f, 2813.975f, 2807.047f, 2800.043f, 2792.962f, 2785.803f, 2778.831f, 2771.785f, 2764.662f, 2757.462f, 2750.450f, 2743.363f, 2736.201f, 2728.963f, 2721.915f, 2714.793f, 2707.597f, 2700.326f, 2693.142f, 2685.883f, 2678.548f, 2671.137f, 2663.919f, 2656.628f, 2649.262f, 2641.820f, 2634.575f, 2627.258f, 2619.867f, 2612.402f, 2605.134f, 2597.795f, 2590.383f, 2582.897f, 2575.606f, 2568.245f, 2560.812f, 2553.307f, 2546.167f, 2538.960f, 2531.686f, 2524.344f, 2517.192f, 2509.974f, 2502.689f, 2495.337f, 2488.179f, 2480.957f, 2473.669f, 2466.315f, 2459.144f, 2451.910f, 2444.612f, 2437.248f, 2430.078f, 2422.845f, 2415.550f, 2408.190f, 2401.381f, 2394.516f, 2387.596f, 2380.619f, 2373.816f, 2366.958f, 2360.046f, 2353.078f, 2346.280f, 2339.429f, 2332.524f, 2325.565f, 2318.776f, 2311.934f, 2305.040f, 2298.092f, 2291.318f, 2284.493f, 2277.617f, 2270.687f, 2263.448f, 2256.151f, 2248.795f, 2241.381f, 2234.163f, 2226.888f, 2219.557f, 2212.168f, 2204.966f, 2197.709f, 2190.396f, 2183.026f, 2175.844f, 2168.607f, 2161.315f, 2153.968f, 2146.816f, 2139.611f, 2132.353f, 2125.041f, 2117.913f, 2110.733f, 2103.500f, 2096.215f, 2089.117f, 2081.968f, 2074.768f, 2067.516f, 2060.454f, 2053.343f, 2046.181f, 2038.970f, 2031.939f, 2024.861f, 2017.733f, 2010.557f, 2003.569f, 1996.534f, 1989.452f, 1982.322f, 1975.376f, 1968.385f, 1961.348f, 1954.264f, 1947.367f, 1940.424f, 1933.437f, 1926.405f, 1919.548f, 1912.647f, 1905.702f, 1898.713f, 1891.907f, 1885.057f, 1878.166f, 1871.231f, 1864.472f, 1857.672f, 1850.831f, 1843.947f, 1836.750f, 1829.507f, 1822.216f, 1814.879f, 1807.746f, 1800.568f, 1793.345f, 1786.076f, 1778.998f, 1771.876f, 1764.711f, 1757.501f, 1750.489f, 1743.435f, 1736.339f, 1729.199f, 1722.250f, 1715.261f, 1708.229f, 1701.157f, 1694.813f, 1688.435f, 1682.024f, 1675.578f, 1669.297f, 1662.983f, 1656.635f, 1650.255f, 1644.036f, 1637.786f, 1631.503f, 1625.189f, 1619.033f, 1612.846f, 1606.628f, 1600.379f, 1594.285f, 1588.162f, 1582.008f, 1575.825f, 1569.494f, 1563.132f, 1556.738f, 1550.311f, 1544.063f, 1537.784f, 1531.475f, 1525.134f, 1518.942f, 1512.721f, 1506.470f, 1500.188f, 1494.078f, 1487.939f, 1481.771f, 1475.573f, 1469.534f, 1463.468f, 1457.373f, 1451.250f, 1445.281f, 1439.285f, 1433.261f, 1427.211f, 1421.309f, 1415.381f, 1409.426f, 1403.445f, 1397.627f, 1391.783f, 1385.914f, 1380.019f, 1374.272f, 1368.500f, 1362.704f, 1356.883f, 1351.213f, 1345.520f, 1339.803f, 1334.062f, 1328.357f, 1322.628f, 1316.875f, 1311.099f, 1305.470f, 1299.819f, 1294.144f, 1288.447f, 1282.890f, 1277.312f, 1271.711f, 1266.088f, 1260.620f, 1255.131f, 1249.621f, 1244.089f, 1238.695f, 1233.280f, 1227.844f, 1222.388f, 1217.159f, 1211.910f, 1206.642f, 1201.355f, 1196.202f, 1191.030f, 1185.839f, 1180.630f, 1175.546f, 1170.444f, 1165.325f, 1160.188f, 1155.189f, 1150.174f, 1145.141f, 1140.092f, 1135.161f, 1130.214f, 1125.251f, 1120.270f, 1115.885f, 1111.486f, 1107.074f, 1102.649f, 1098.316f, 1093.971f, 1089.613f, 1085.242f, 1080.988f, 1076.722f, 1072.444f, 1068.154f, 1063.948f, 1059.729f, 1055.499f, 1051.257f, 1047.124f, 1042.979f, 1038.824f, 1034.657f, 1030.149f, 1025.628f, 1021.093f, 1016.545f, 1012.107f, 1007.656f, 1003.193f, 998.716f, 994.364f, 990.000f, 985.623f, 981.234f, 976.946f, 972.646f, 968.335f, 964.011f, 959.791f, 955.559f, 951.316f, 947.061f, 942.833f, 938.594f, 934.344f, 930.082f, 925.939f, 921.785f, 917.620f, 913.445f, 909.364f, 905.272f, 901.170f, 897.057f, 893.054f, 889.040f, 885.016f, 880.983f, 877.046f, 873.100f, 869.145f, 865.180f, 861.259f, 857.329f, 853.389f, 849.439f, 845.576f, 841.704f, 837.822f, 833.932f, 830.142f, 826.344f, 822.537f, 818.721f, 815.008f, 811.287f, 807.557f, 803.819f, 800.170f, 796.514f, 792.850f, 789.177f, 785.425f, 781.665f, 777.896f, 774.119f, 770.433f, 766.739f, 763.037f, 759.327f, 755.710f, 752.085f, 748.452f, 744.811f, 741.265f, 737.711f, 734.149f, 730.580f, 727.107f, 723.626f, 720.138f, 716.643f, 713.482f, 710.316f, 707.143f, 703.965f, 700.870f, 697.769f, 694.663f, 691.552f, 688.510f, 685.462f, 682.409f, 679.351f, 676.363f, 673.370f, 670.371f, 667.367f, 664.450f, 661.528f, 658.601f, 655.668f, 652.439f, 649.203f, 645.961f, 642.713f, 639.567f, 636.416f, 633.259f, 630.096f, 627.006f, 623.910f, 620.808f, 617.701f, 614.683f, 611.660f, 608.631f, 605.597f, 602.637f, 599.672f, 596.702f, 593.727f, 591.113f, 588.496f, 585.875f, 583.249f, 580.685f, 578.116f, 575.543f, 572.967f, 570.452f, 567.933f, 565.410f, 562.884f, 560.419f, 557.951f, 555.479f, 553.004f, 550.590f, 548.173f, 545.753f, 543.330f, 540.690f, 538.046f, 535.398f, 532.746f, 530.173f, 527.596f, 525.016f, 522.431f, 519.909f, 517.384f, 514.855f, 512.323f, 509.870f, 507.414f, 504.955f, 502.492f, 500.077f, 497.658f, 495.236f, 492.810f, 490.652f, 488.490f, 486.326f, 484.160f, 482.042f, 479.921f, 477.798f, 475.672f, 473.595f, 471.516f, 469.434f, 467.349f, 465.314f, 463.277f, 461.237f, 459.194f, 457.202f, 455.207f, 453.209f, 451.210f, 449.433f, 447.654f, 445.873f, 444.090f, 442.358f, 440.624f, 438.889f, 437.152f, 435.448f, 433.742f, 432.035f, 430.326f, 428.651f, 426.974f, 425.295f, 423.615f, 421.986f, 420.356f, 418.724f, 417.091f, 415.245f, 413.397f, 411.547f, 409.696f, 407.878f, 406.058f, 404.237f, 402.413f, 400.641f, 398.868f, 397.092f, 395.315f, 393.590f, 391.863f, 390.134f, 388.404f, 386.708f, 385.010f, 383.311f, 381.610f, 379.872f, 378.132f, 376.391f, 374.647f, 372.957f, 371.264f, 369.571f, 367.875f, 366.214f, 364.552f, 362.888f, 361.223f, 359.593f, 357.961f, 356.327f, 354.693f, 353.111f, 351.528f, 349.944f, 348.358f, 346.789f, 345.219f, 343.648f, 342.075f, 340.538f, 338.999f, 337.459f, 335.918f, 334.412f, 332.905f, 331.397f, 329.888f, 328.414f, 326.939f, 325.463f, 323.986f, 322.545f, 321.102f, 319.659f, 318.214f, 316.806f, 315.396f, 313.985f, 312.573f, 311.198f, 309.821f, 308.443f, 307.065f, 305.741f, 304.416f, 303.091f, 301.764f, 300.437f, 299.108f, 297.779f, 296.449f, 295.174f, 293.898f, 292.621f, 291.344f, 290.084f, 288.824f, 287.563f, 286.301f, 285.057f, 283.812f, 282.566f, 281.320f, 280.110f, 278.900f, 277.689f, 276.477f, 275.303f, 274.127f, 272.951f, 271.774f, 270.616f, 269.457f, 268.297f, 267.136f, 265.994f, 264.851f, 263.707f, 262.563f, 261.437f, 260.310f, 259.183f, 258.055f, 256.965f, 255.874f, 254.782f, 253.690f, 252.616f, 251.542f, 250.467f, 249.392f, 248.335f, 247.278f, 246.220f, 245.161f, 244.140f, 243.119f, 242.098f, 241.075f, 240.072f, 239.068f, 238.063f, 237.058f, 236.053f, 235.047f, 234.040f, 233.033f, 232.064f, 231.094f, 230.125f, 229.154f, 228.203f, 227.251f, 226.298f, 225.346f, 224.412f, 223.477f, 222.543f, 221.607f, 220.691f, 219.775f, 218.858f, 217.940f, 217.042f, 216.143f, 215.244f, 214.344f, 213.464f, 212.583f, 211.702f, 210.820f, 209.958f, 209.095f, 208.232f, 207.368f, 206.524f, 205.679f, 204.834f, 203.988f, 203.162f, 202.336f, 201.509f, 200.682f, 199.854f, 199.026f, 198.198f, 197.369f, 196.580f, 195.790f, 195.000f, 194.210f, 193.419f, 192.628f, 191.836f, 191.045f, 190.292f, 189.539f, 188.786f, 188.033f, 187.260f, 186.486f, 185.712f, 184.938f, 184.203f, 183.468f, 182.732f, 181.996f, 181.280f, 180.564f, 179.847f, 179.130f, 178.413f, 177.696f, 176.978f, 176.260f, 175.562f, 174.863f, 174.164f, 173.465f, 172.786f, 172.107f, 171.427f, 170.747f, 170.067f, 169.386f, 168.705f, 168.024f, 167.363f, 166.702f, 166.040f, 165.379f, 164.737f, 164.094f, 163.452f, 162.809f, 162.167f, 161.524f, 160.880f, 160.237f, 159.613f, 158.990f, 158.366f, 157.742f, 157.137f, 156.533f, 155.929f, 155.324f, 154.719f, 154.114f, 153.509f, 152.903f, 152.318f, 151.732f, 151.146f, 150.560f, 149.974f, 149.388f, 148.801f, 148.214f, 147.648f, 147.081f, 146.514f, 145.947f, 145.400f, 144.853f, 144.305f, 143.758f, 143.210f, 142.663f, 142.115f, 141.567f, 141.018f, 140.470f, 139.921f, 139.373f, 138.844f, 138.316f, 137.787f, 137.258f, 136.729f, 136.200f, 135.670f, 135.141f, 134.631f, 134.122f, 133.613f, 133.103f, 132.613f, 132.124f, 131.634f, 131.145f, 130.655f, 130.165f, 129.675f, 129.184f, 128.694f, 128.203f, 127.713f, 127.222f, 126.752f, 126.281f, 125.811f, 125.340f, 124.869f, 124.398f, 123.927f, 123.456f, 123.005f, 122.554f, 122.103f, 121.652f, 121.201f, 120.750f, 120.298f, 119.847f, 119.416f, 118.984f, 118.553f, 118.122f, 117.690f, 117.259f, 116.827f, 116.395f, 115.964f, 115.532f, 115.100f, 114.667f, 114.256f, 113.844f, 113.432f, 113.020f, 112.608f, 112.196f, 111.784f, 111.372f, 110.980f, 110.589f, 110.197f, 109.805f, 109.392f, 108.979f, 108.567f, 108.154f, 107.782f, 107.410f, 107.039f, 106.667f, 106.274f, 105.881f, 105.489f, 105.096f, 104.724f, 104.351f, 103.979f, 103.606f, 103.234f, 102.861f, 102.489f, 102.116f, 101.764f, 101.412f, 101.059f, 100.707f, 100.355f, 100.002f, 99.650f, 99.297f, 98.945f, 98.592f, 98.240f, 97.887f, 97.534f, 97.181f, 96.828f, 96.475f, 96.143f, 95.810f, 95.478f, 95.145f, 94.813f, 94.480f, 94.148f, 93.815f, 93.482f, 93.149f, 92.817f, 92.484f, 92.172f, 91.859f, 91.547f, 91.235f, 90.922f, 90.610f, 90.298f, 89.985f,];
        private readonly ILogger _logger;
        private readonly IOptions<InovaGate1TemperatureSensorOptions> _options;
        private readonly McuManager _manager;
        private readonly string _name;
        private readonly ReferenceCounter _validationSupressor;
        private readonly McuSpi _spi;
        private readonly TaskQueue _readEventQueue;
        private readonly object _shutdownReadSync = new();
        private readonly PrimitiveDeque<(float Temperature, SystemTimestamp Timestamp)> _averageQueue;
        private McuSendResult? _shutdownReadSend;
        private Timer? _shutdownReadTimer;
        private bool _hasStatedShutdownReadTimer;
        private int _oid;
        private McuCommand _queryCmdResponse = default!;
        private int _queryCmdResponseOid;
        private int _queryCmdResponseNextClock;
        private int _queryCmdResponseValue;
        private int _queryCmdResponseFault;
        private int _reportClock;
        private McuCommand _readCmd = default!;
        private volatile McuTemperatureSensorData? _lastValue;

        public IMcu Mcu => _spi.Mcu;
        public AsyncEvent<McuTemperatureSensorData> ReadEvent { get; } = new();
        public McuTemperatureSensorData? CurrentValue => _lastValue;
        public bool IsValidationSupressed => _validationSupressor.IsIncremented;

        static InovaGate1TemperatureSensor()
        {
            Array.Reverse(s_resTable);
        }

        public InovaGate1TemperatureSensor(
            IOptions<InovaGate1TemperatureSensorOptions> options,
            McuManager manager,
            string name)
        {
            _logger = manager.CreateLogger<InovaGate1TemperatureSensor>();
            _options = options;
            _manager = manager;
            _name = name;
            _readEventQueue = new();

            var o = options.Value;
            _averageQueue = new();
            _validationSupressor = new();
            _spi = new McuSpi(
                manager.ClaimBus(o.Bus, shareType: nameof(InovaGate1TemperatureSensor)),
                manager.ClaimPin(McuPinType.ChipSelect, o.CsPin, shareType: nameof(InovaGate1TemperatureSensor)),
                mode: 0,
                rate: o.SpiSpeed);

            Mcu.RegisterConfigCommand(BuildConfig);
            manager.RegisterSetup(Mcu, OnSetup);

            manager.RunningCancel.Register(() =>
            {
                lock (_shutdownReadSync)
                {
                    if (_shutdownReadTimer != null)
                    {
                        _shutdownReadTimer.Dispose();
                        _shutdownReadTimer = null;
                    }
                }
            });
            manager.ShutdownEvent.AddHandler(OnShutdown);
            CheckShutdownReadTimer();
        }

        private ValueTask OnShutdown(McuShutdownMessage message, CancellationToken token)
        {
            CheckShutdownReadTimer();
            return ValueTask.CompletedTask;
        }

        /// <summary>
        /// Keeps forcefully reading sensor temperatures even after MCU shutdown, when timers are disabled.
        /// </summary>
        /// <remarks>
        /// This helps to monitor printer temperature in case some heater were left enabled by MCU freezing/shutting down.
        /// </remarks>
        private void CheckShutdownReadTimer()
        {
            lock (_shutdownReadSync)
            {
                var options = _options.Value;
                if (_manager.RunningCancel.IsCancellationRequested)
                    return;
                if (!_hasStatedShutdownReadTimer && _manager.IsShutdown)
                {
                    _hasStatedShutdownReadTimer = true;
                    _shutdownReadTimer = new Timer(state =>
                    {
                        try
                        {
                            lock (_shutdownReadSync)
                            {
                                if (_readCmd != null)
                                    _shutdownReadSend = Mcu.Send(_readCmd, McuCommandPriority.Default, McuOccasion.Now, cancelFirst: _shutdownReadSend);
                            }
                        }
                        catch (Exception ex)
                        {
                            if (!Mcu.HasLostCommunication)
                                _logger.LogError(ex, $"Failed to read temperature sensor {_name} in shutdown, will keep on reading since communication with MCU {Mcu} is not yet lost");
                            else
                            {
                                _logger.LogError(ex, $"Failed to read temperature sensor {_name} in shutdown, will stop, since communication with {Mcu} is lost");
                                lock (_shutdownReadSync)
                                {
                                    if (_shutdownReadTimer != null)
                                    {
                                        _shutdownReadTimer.Dispose();
                                        _shutdownReadTimer = null;
                                    }                                    
                                }
                            }
                        }
                    }, null, options.ReadPeriod, options.ReadPeriod);
                }
            }
        }

        private ValueTask OnSetup(CancellationToken token)
        {
            var options = _options.Value;
            _reportClock = (int)Mcu.ClockSync.GetClockDuration(options.ReadPeriod);
            Mcu.RegisterResponseHandler(null, _queryCmdResponse, OnResponse);
            var cmd = Mcu.LookupCommand("query_thermocouple oid=%c clock=%u rest_ticks=%u min_value=%u max_value=%u")
                .Bind("oid", _oid)
                .Bind("clock", Mcu.GetQuerySlotClock(_oid))
                .Bind("rest_ticks", _reportClock)
                .Bind("min_value", CalcAdc(options.MinTemperature))
                .Bind("max_value", CalcAdc(options.MaxTemperature));
            Mcu.Send(cmd, McuCommandPriority.Default, McuOccasion.Now);
            return ValueTask.CompletedTask;
        }

        private static float CalcTemperature(int adc)
        {
            var idx = Array.BinarySearch(s_resTable, adc);
            if (idx < 0)
                idx = ~idx;
            if (idx >= s_resTable.Length)
                idx--;
            if (idx > 0 && Math.Abs(s_resTable[idx - 1] - adc) < Math.Abs(s_resTable[idx] - adc))
                idx--;
            idx = s_resTable.Length - 1 - idx;
            return s_tempTable[idx];
        }

        private static int CalcAdc(float temperature)
        {
            var idx = Array.BinarySearch(s_tempTable, temperature);
            if (idx < 0)
                idx = ~idx;
            if (idx >= s_tempTable.Length)
                idx--;
            if (idx > 0 && Math.Abs(s_tempTable[idx - 1] - temperature) < Math.Abs(s_tempTable[idx] - temperature))
                idx--;
            idx = s_resTable.Length - 1 - idx;
            return (int)s_resTable[idx];
        }

        private ValueTask BuildConfig(McuConfigCommands commands, CancellationToken token)
        {
            var options = _options.Value;
            _oid = commands.CreateOid();
            commands.Add(
                Mcu.LookupCommand("config_thermocouple oid=%c spi_oid=%c thermocouple_type=%c channel=%c")
                .Bind(
                    _oid,
                    _spi.Oid,
                    Mcu.Config.GetThermocoupleType("InovaGATE1"),
                    options.Channel));
            _queryCmdResponse = Mcu.LookupCommand("thermocouple_result oid=%c next_clock=%u value=%u fault=%c");
            _queryCmdResponseOid = _queryCmdResponse.GetArgumentIndex("oid");
            _queryCmdResponseNextClock = _queryCmdResponse.GetArgumentIndex("next_clock");
            _queryCmdResponseValue = _queryCmdResponse.GetArgumentIndex("value");
            _queryCmdResponseFault = _queryCmdResponse.GetArgumentIndex("fault");

            _readCmd = Mcu.LookupCommand("read_thermocouple oid=%c")
                .Bind(_oid);
            return ValueTask.CompletedTask;
        }

        private ValueTask OnResponse(Exception? exception, McuCommand? command, CancellationToken cancel)
        {
            if (command != null && command[_queryCmdResponseOid].UInt32 == _oid)
            {
                //var isFault = command[_queryCmdResponseFault].Int32 != 0; // NOTE: fault not used with InovaGATE1
                var options = _options.Value;
                var timestamp = SystemTimestamp.Now;
                var rawTemperature = CalcTemperature(command[_queryCmdResponseValue].Int32);
                var evictTimestamp = timestamp - options.AverageDuration;
                while (_averageQueue.Count > 0 && _averageQueue.PeekFront().Timestamp <= evictTimestamp)
                    _averageQueue.PopFront();
                var sum = 0.0f;
                _averageQueue.PushBack((rawTemperature, timestamp));
                for (int i = 0; i < _averageQueue.Count; i++)
                    sum += _averageQueue[i].Temperature;
                var avgTemperature = sum / _averageQueue.Count;
                var value = new McuTemperatureSensorData(avgTemperature, timestamp);
                _lastValue = value;
                _readEventQueue.EnqueueValue(() => ReadEvent.Invoke(value, _manager.RunningCancel), null);
            }
            return ValueTask.CompletedTask;
        }

        public IDisposable SupressValidation()
            => _validationSupressor.Increment();

        public override string ToString()
            => $"{_name} [InovaGate1]";
    }
}
