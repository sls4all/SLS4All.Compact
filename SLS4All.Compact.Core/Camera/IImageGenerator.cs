using SLS4All.Compact.IO;
using SLS4All.Compact.Threading;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace SLS4All.Compact.Camera
{
    public interface IImageGenerator
    {
        bool TryGetLastImage(out MimeData data);
        AsyncEvent<MimeData> ImageCaptured { get; }
    }
}
