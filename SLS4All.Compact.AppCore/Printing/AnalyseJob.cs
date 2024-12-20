// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.

using SLS4All.Compact.Storage;
using SLS4All.Compact.Storage.PrintJobs;
using SLS4All.Compact.Storage.PrintProfiles;
using SLS4All.Compact.Validation;

namespace SLS4All.Compact.Printing
{
    public sealed class AnalyseJob : IPrintJob
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public JobObjectFile[] ObjectFiles
        {
            get => [];
            set { }
        }
        public IPrintJobNestingSnapshot NestingState
        {
            get => NullNestingSnapshot.Instance;
            set { }
        }
        public decimal? AvailableDepth => null;
        public PrintJobType Type => PrintJobType.AnalyseHeating;

        public required string Name { get; set; }
        public required PrintProfileReference PrintProfile { get; set; }
        public DateTimeOffset? CreatedAt { get; set; }

        public bool NeedsLaser => false;
        public bool DryPrintEnabled => false;
        public bool PreviewEnabled => false;

        public IStorageObject Clone()
            => (AnalyseJob)MemberwiseClone();
        public void MergeFrom(IStorageObject other)
            => throw new NotSupportedException();
        public ValueTask<ValidationHelper> Validate(ValidationContext context)
            => new ValueTask<ValidationHelper>(new ValidationHelper(context));
    }
}
