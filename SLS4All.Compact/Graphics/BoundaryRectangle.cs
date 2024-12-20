// Copyright(C) 2024 anyteq development s.r.o.
// 
// This file is part of SLS4All project (sls4all.com) and is made available
// under the terms of the License Agreement as described in the LICENSE.txt
// file located in the root directory of the repository.


namespace SLS4All.Compact.Graphics
{
    public readonly record struct BoundaryRectangle(int MinX, int MinY, int MaxX, int MaxY)
    {
        public int Width => MaxX - MinX + 1;
        public int Height => MaxY - MinY + 1;

        public BoundaryRectangle OffsetInTopLeft(in BoundaryRectangle outerBoundary)
            => new BoundaryRectangle(MinX + outerBoundary.MinX, MinY + outerBoundary.MinY, MinX + outerBoundary.MaxX, MinY + outerBoundary.MaxY);

        public BoundaryRectangle Offset(int x, int y)
            => new BoundaryRectangle(MinX + x, MinY + y, MaxX + x, MaxY + y);

        public static BoundaryRectangle Min(BoundaryRectangle a, BoundaryRectangle b)
            => new BoundaryRectangle(Math.Max(a.MinX, b.MinX), Math.Max(a.MinY, b.MinY), Math.Min(a.MaxX, b.MaxX), Math.Min(a.MaxY, b.MaxY));

        public static BoundaryRectangle Max(BoundaryRectangle a, BoundaryRectangle b)
            => new BoundaryRectangle(Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY));
    }
}
