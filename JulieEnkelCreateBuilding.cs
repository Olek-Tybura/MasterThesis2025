using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Master_Thesis;
namespace MasterThesisV2
{
    public class JulieEnkelCreateBuilding : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the JulieEnkelCreateBuilding class.
        /// </summary>
        public JulieEnkelCreateBuilding()
          : base("CreateBuilding", "Nickname",
              "Description",
              "MasterThesis", "V4")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "", "", GH_ParamAccess.item);
            
            pManager.AddNumberParameter("x-spacing", "", "", GH_ParamAccess.item);
            pManager.AddNumberParameter("y-spacing", "", "", GH_ParamAccess.item);
            pManager.AddNumberParameter("FloorHeight", "", "", GH_ParamAccess.item);

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Building", "", "", GH_ParamAccess.list);
         
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Brep brep = null;
            double xSpacing = 0, ySpacing = 0, FloorHeight = 0;

            if (!DA.GetData(0, ref brep)) return;
            if (!DA.GetData(1, ref xSpacing)) return;
            if (!DA.GetData(2, ref ySpacing)) return;
            if (!DA.GetData(3, ref FloorHeight)) return;

            Building building = new Building();

            BoundingBox bbox = brep.GetBoundingBox(true);
            Point3d startpoint = new Point3d(bbox.Min.X, bbox.Min.Y, bbox.Min.Z);

            double minX = bbox.Min.X, maxX = bbox.Max.X;
            double minY = bbox.Min.Y, maxY = bbox.Max.Y;
            double minZ = bbox.Min.Z, maxZ = bbox.Max.Z;

            List<Point3d> gridPoints = new List<Point3d>();
            for (double i = minX; i <= maxX; i += xSpacing)
            {
                for (double j = minY; j <= maxY; j += ySpacing)
                {
                    for (double k = minZ; k <= maxZ; k += FloorHeight)
                    {
                        Point3d gridPoint = new Point3d(i, j, k);
                        gridPoints.Add(gridPoint);
                    }
                }
            }
            var columns = GenerateColumnsFromPoints(gridPoints);

            var beams = FilterBeamsInsideOrOnBrep(GenerateBeamsFromPointsSingleBrep(gridPoints), brep);
            building.Beams = beams;

            List<Beam> xBeams, yBeams;
            SplitBeamsByDirection(beams, out xBeams, out yBeams); //Splitter bjelker i x og y-retning
            SortBeamsByLengthDominance(xBeams, yBeams, out var primaryBeams, out var secondaryBeams); //Sorterer primary and secondary
            SplitPrimaryIntoMiddleAndEdgeByBoundingBoxSingleBrep(primaryBeams, brep, out var middleBeams, out var edgeBeams); //Finner edge og interalbeams. 

            //Setter opp BeamSublists:
            building.BeamSublists = new List<List<Beam>>
            {
                middleBeams, // Index 0 = Internal beams
                edgeBeams,   // Index 1 = Edge beams
                secondaryBeams // Index 2 = Secondary beams
            };

            var slabs = GenerateSlabsBetweenPoints(gridPoints, xSpacing, ySpacing);

            building.Columns = columns;
            building.Slabs = slabs;

            DA.SetData(0, building);
         


        }
        public static List<Column> GenerateColumnsFromPoints(List<Point3d> points, double tolerance = 0.001)
        {
            var columns = new List<Column>(); //Liste til søylene

            // Gruppér punktene etter X/Y (med avrunding for robust matching)
            var groups = points.GroupBy(pt => new {
                X = Math.Round(pt.X / tolerance) * tolerance,
                Y = Math.Round(pt.Y / tolerance) * tolerance
            });

            foreach (var group in groups)
            {
                var sorted = group.OrderBy(p => p.Z).ToList();
                for (int i = 0; i < sorted.Count - 1; i++)
                {
                    // Lag en søyle mellom to påfølgende Z-nivåer
                    var line = new Line(sorted[i], sorted[i + 1]);
                    columns.Add(new Column(line)); // Legg til søyle i listen
                }
            }

            return columns;
        }
        public static List<Beam> FilterBeamsInsideOrOnBrep(List<Beam> beams, Brep brep, double tolerance = 0.01) //for en brep
        {
            return beams
                .Where(beam =>
                {
                    Point3d midpoint = beam.Axis.PointAt(0.5);
                    return brep.IsPointInside(midpoint, tolerance, true) ||
                           IsPointOnAnySurface(midpoint, brep, tolerance);
                })
                .ToList();
        }
        public static List<Beam> GenerateBeamsFromPointsSingleBrep(List<Point3d> points, double tolerance = 0.001) //For en brep
        {
            var beams = new List<Beam>();

            // 1. Gruppér punkter etter Z-nivå (avrundet for robusthet)
            var levels = points
                .GroupBy(p => Math.Round(p.Z / tolerance) * tolerance)
                .OrderBy(zGroup => zGroup.Key)
                .ToList();

            if (levels.Count <= 1)
                return beams;  // Ingen bjelker hvis bare ett nivå

            // 2. Hopp over nederste nivå (første i sortert liste)
            foreach (var level in levels.Skip(1))
            {
                var pointsOnLevel = level.ToList();

                // 2A. Gruppér etter Y for X-bjelker
                var yGroups = pointsOnLevel.GroupBy(p => Math.Round(p.Y / tolerance) * tolerance);
                foreach (var yGroup in yGroups)
                {
                    var xSorted = yGroup.OrderBy(p => p.X).ToList();
                    for (int i = 0; i < xSorted.Count - 1; i++)
                        beams.Add(new Beam(new Line(xSorted[i], xSorted[i + 1])));
                }

                // 2B. Gruppér etter X for Y-bjelker
                var xGroups = pointsOnLevel.GroupBy(p => Math.Round(p.X / tolerance) * tolerance);
                foreach (var xGroup in xGroups)
                {
                    var ySorted = xGroup.OrderBy(p => p.Y).ToList();
                    for (int i = 0; i < ySorted.Count - 1; i++)
                        beams.Add(new Beam(new Line(ySorted[i], ySorted[i + 1])));
                }
            }

            return beams;
        }
        private static bool IsPointOnAnySurface(Point3d pt, Brep brep, double tolerance)
        {
            foreach (var face in brep.Faces)
            {
                var srf = face.UnderlyingSurface();
                if (srf.ClosestPoint(pt, out double u, out double v))
                {
                    var closest = srf.PointAt(u, v);
                    if (pt.DistanceTo(closest) < tolerance)
                        return true;
                }
            }
            return false;
        }
        public static void SplitBeamsByDirection(List<Beam> beams, out List<Beam> xBeams, out List<Beam> yBeams, double tolerance = 1e-3)
        {
            xBeams = new List<Beam>();
            yBeams = new List<Beam>();

            foreach (var beam in beams)
            {
                Vector3d dir = beam.Axis.Direction;
                dir.Unitize();

                if (Math.Abs(dir.Y) < tolerance)
                    xBeams.Add(beam);
                else if (Math.Abs(dir.X) < tolerance)
                    yBeams.Add(beam);
            }
        }
        public static void SortBeamsByLengthDominance(List<Beam> xBeams, List<Beam> yBeams, out List<Beam> primaryBeams, out List<Beam> secondaryBeams)
        {
            double avgX = xBeams.Count > 0 ? xBeams.Average(b => b.Axis.Length) : 0;
            double avgY = yBeams.Count > 0 ? yBeams.Average(b => b.Axis.Length) : 0;

            if (avgX >= avgY)
            {
                primaryBeams = new List<Beam>(xBeams);
                secondaryBeams = new List<Beam>(yBeams);
            }
            else
            {
                primaryBeams = new List<Beam>(yBeams);
                secondaryBeams = new List<Beam>(xBeams);
            }
        }
        public static void SplitPrimaryIntoMiddleAndEdgeByBoundingBoxSingleBrep(
            List<Beam> primaryBeams,
            Brep brep,
            out List<Beam> middleBeams,
            out List<Beam> edgeBeams,
            double edgeTolerance = 0.1)
        {
            middleBeams = new List<Beam>();
            edgeBeams = new List<Beam>();

            if (primaryBeams.Count == 0)
                return;

            // Finn primary retning:
            Vector3d primaryDir = primaryBeams[0].Axis.Direction;
            primaryDir.Unitize();
            bool isXPrimary = Math.Abs(primaryDir.X) > Math.Abs(primaryDir.Y);

            // Finn min og max basert på BEAM-coordinater:
            double minCoord = primaryBeams.Min(b => isXPrimary ? b.Axis.From.Y : b.Axis.From.X);
            double maxCoord = primaryBeams.Max(b => isXPrimary ? b.Axis.From.Y : b.Axis.From.X);

            foreach (var beam in primaryBeams)
            {
                double fromCoord = isXPrimary ? beam.Axis.From.Y : beam.Axis.From.X;
                double toCoord = isXPrimary ? beam.Axis.To.Y : beam.Axis.To.X;
                double midCoord = isXPrimary ? beam.Axis.PointAt(0.5).Y : beam.Axis.PointAt(0.5).X;

                // Sjekk hvor nær bjelken er til min eller max av ALLE bjelker:
                bool nearMin = Math.Abs(fromCoord - minCoord) < edgeTolerance ||
                               Math.Abs(toCoord - minCoord) < edgeTolerance ||
                               Math.Abs(midCoord - minCoord) < edgeTolerance;

                bool nearMax = Math.Abs(fromCoord - maxCoord) < edgeTolerance ||
                               Math.Abs(toCoord - maxCoord) < edgeTolerance ||
                               Math.Abs(midCoord - maxCoord) < edgeTolerance;

                if (nearMin || nearMax)
                    edgeBeams.Add(beam);
                else
                    middleBeams.Add(beam);
            }

        }
        public static List<Slab> GenerateSlabsBetweenPoints(List<Point3d> points, double xSpacing, double ySpacing, double tolerance = 0.001)
        {
            var slabs = new List<Slab>();

            // Gruppér punktene etter Z-nivå og hopp over det laveste nivået (fundament)
            var levels = points
                .GroupBy(p => Math.Round(p.Z / tolerance) * tolerance)
                .OrderBy(g => g.Key)
                .Skip(1);  // Skipper første (laveste) nivå

            foreach (var level in levels)
            {
                var pts = level.ToList();

                // Gruppér etter Y for X-retning
                var yGroups = pts.GroupBy(p => Math.Round(p.Y / tolerance) * tolerance).ToList();

                for (int yg = 0; yg < yGroups.Count - 1; yg++)
                {
                    var currentYGroup = yGroups[yg].OrderBy(p => p.X).ToList();
                    var nextYGroup = yGroups[yg + 1].OrderBy(p => p.X).ToList();

                    int count = Math.Min(currentYGroup.Count, nextYGroup.Count) - 1;
                    for (int i = 0; i < count; i++)
                    {
                        var p1 = currentYGroup[i];
                        var p2 = currentYGroup[i + 1];
                        var p3 = nextYGroup[i + 1];
                        var p4 = nextYGroup[i];

                        if (p1.DistanceTo(p2) > xSpacing * 1.1 || p1.DistanceTo(p4) > ySpacing * 1.1)
                            continue; // Hopper over hvis punktene ikke matcher gridet

                        var slabSurface = NurbsSurface.CreateFromCorners(p1, p2, p3, p4);
                        if (slabSurface != null)
                            slabs.Add(new Slab(slabSurface.ToBrep(), 0.2, p1.Z));  // 0.2 = tykkelse
                    }
                }
            }

            return slabs;
        }


        /// <summary>
        /// Provides an Icon for the component.
        /// </summary>
        protected override System.Drawing.Bitmap Icon
        {
            get
            {
                //You can add image files to your project resources and access them like this:
                // return Resources.IconForThisComponent;
                return null;
            }
        }

        /// <summary>
        /// Gets the unique ID for this component. Do not change this ID after release.
        /// </summary>
        public override Guid ComponentGuid
        {
            get { return new Guid("8247387C-9DE6-4FC6-A866-FB6D7989712C"); }
        }
    }
}