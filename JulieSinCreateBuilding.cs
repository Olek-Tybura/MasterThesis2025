using System;
using System.Collections.Generic;
using System.Linq;
using Grasshopper.Kernel;
using Rhino.Geometry;

using Master_Thesis;
using CommunityToolkit.HighPerformance;
using Eto.Forms;
using Karamba.Geometry;
using OfficeOpenXml.FormulaParsing.Excel.Functions.Math;
using System.Diagnostics.Metrics;



namespace MasterThesisV2
{
    public class JulieSinCreateBuilding : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the JulieSinCreateBuilding class.
        /// </summary>
        public JulieSinCreateBuilding()
          : base("CreateAdvancedBuilding", "Nickname",
              "Description",
              "MasterThesis", "V4")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddBrepParameter("Breps", "B", "Building volumes", GH_ParamAccess.list);
            pManager.AddPointParameter("Starting Point", "SP", "", GH_ParamAccess.item, new Point3d(0, 0, 0));
            pManager.AddNumberParameter("X spacing", "XS", "Spacing between columns in X direction", GH_ParamAccess.item);
            pManager.AddNumberParameter("Y spacing", "YS", "Spacing between columns in Y direction", GH_ParamAccess.item);
            pManager.AddNumberParameter("Floor height", "FH", "Height of each floor", GH_ParamAccess.item);
            pManager.AddBooleanParameter("FilterCornerPoints", "", "", GH_ParamAccess.item, true);
           

        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Building", "", "", GH_ParamAccess.item);
          
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Brep> breps = new List<Brep>();
            Point3d basePoint = Point3d.Origin;
            double xSpac = 0, ySpac = 0, fh = 0;

            DA.GetDataList(0, breps);
            DA.GetData(1, ref basePoint);
            DA.GetData(2, ref xSpac);
            DA.GetData(3, ref ySpac);
            DA.GetData(4, ref fh);

            var grupper = GroupIntersectingBreps(breps);
            var allGridPoints = new List<Point3d>();
            var allintersectionPoints = new List<Point3d>();
            var allCornerPoints = new List<Point3d>();

            Building building = new Building();


            foreach (var gruppe in grupper)
            {
                Point3d localStart = FindstartPointForGroup(gruppe); //Finner lokalt startpunkt for hver gruppe
                Point3d justert = localStart + new Vector3d(basePoint.X, basePoint.Y, 0); //Lager sammenheng mellom startingpoint og lokal startpunkt

                var grid = Generate3DGrid(gruppe, justert, xSpac, ySpac, fh);
                allGridPoints.AddRange(grid.ConvertAll(gp => gp.Point));


                var gridLines = CreateLinesThroughGridPoints(grid, 10); // ekstra lengde 10 i begge retninger
                var intersectionPoints = FindIntersectionsWithBrep(gridLines, gruppe, 0.001); // Finn kryssingspunktene
                allintersectionPoints.AddRange(intersectionPoints); // Legg til kryssingspunktene i gridet

                var cornerPts = GenerateCornerPointsPerBrep(gruppe, fh);
                allCornerPoints.AddRange(cornerPts);

            }

            double distanceThreshold = 1.2;
            bool keepAll = true;
            DA.GetData(5, ref keepAll);

            var gridPointsForBeams = new List<Point3d>(allGridPoints);
            var cornerPointsForBeams = new List<Point3d>(allCornerPoints);
            var intersectionPointsForBeams = new List<Point3d>(allintersectionPoints);

            // Fjern intersectionpunkter som overlapper med corner
            allintersectionPoints = allintersectionPoints
                .Where(ip => !allCornerPoints.Any(cp => cp.DistanceTo(ip) < 0.001))
                .ToList();

            // Fjern intersectionPoints som overlapper med hovedgrid før videre filtrering
            allintersectionPoints = allintersectionPoints
                .Where(ip => !allGridPoints.Any(gp => gp.DistanceTo(ip) < 0.001)) // eller annen lav toleranse
                .ToList();

            // Fjern hjørnepunkter som overlapper med hovedgrid
            allCornerPoints = allCornerPoints
                .Where(cp => !allGridPoints.Any(gp => gp.DistanceTo(cp) < 0.001))
                .ToList();

            // Kopier originalen før filtrering
            var originalIntersections = new List<Point3d>(allintersectionPoints);



            if (!keepAll)
            {
                // Trinn 1: Finn intersection-punkter nær hovedgrid
                var intersectCloseToGrid = allintersectionPoints
                    .Where(ip => allGridPoints.Any(gp => gp.DistanceTo(ip) <= distanceThreshold))
                    .ToList();

                // Trinn 2: Fjern disse fra intersection-punktene
                allintersectionPoints = allintersectionPoints
                    .Except(intersectCloseToGrid)
                    .ToList();

                // Trinn 3: Bruk ORIGINALE intersection-punkter til å filtrere corner-punktene
                allCornerPoints = allCornerPoints
                    .Where(cp => !originalIntersections.Any(ip => cp.DistanceTo(ip) <= distanceThreshold))
                    .ToList();

                // Trinn 4: Fjern corner-punkter som er nær hovedgrid-punktene
                allCornerPoints = allCornerPoints
                    .Where(cp => !allGridPoints.Any(gp => cp.DistanceTo(gp) <= distanceThreshold))
                    .ToList();
            }
            else
            {
                // True: Fjern grid-punkter nær intersection-punkter
                allGridPoints = allGridPoints
                    .Where(gp => !allintersectionPoints.Any(ip => gp.DistanceTo(ip) <= distanceThreshold))
                    .ToList();

                // Fjern grid-punkter som ligger ≤ 1 m fra corner-punktene
                allGridPoints = allGridPoints
                    .Where(gp => !allCornerPoints.Any(cp => gp.DistanceTo(cp) <= distanceThreshold))
                    .ToList();

                // Fjern intersection-punkter som ligger ≤ 1 m fra corner-punktene
                allintersectionPoints = allintersectionPoints
                    .Where(ip => !allCornerPoints.Any(cp => cp.DistanceTo(ip) <= distanceThreshold))
                    .ToList();


            }

            var samletPunkter = new List<Point3d>();
            samletPunkter.AddRange(allGridPoints.Concat(allintersectionPoints).Concat(allCornerPoints)); //Legger til alle punktene i en liste
            samletPunkter = Point3d.CullDuplicates(samletPunkter, 0.001).ToList(); //Fjerner duplikater

            //Bjelker
            var gridForBeams = gridPointsForBeams
            .Where(gp =>
                !intersectionPointsForBeams.Any(ip => gp.DistanceTo(ip) <= distanceThreshold) &&
                !cornerPointsForBeams.Any(cp => gp.DistanceTo(cp) <= distanceThreshold))
            .ToList();

            var intersectionForBeams = intersectionPointsForBeams
                .Where(ip => !cornerPointsForBeams.Any(cp => cp.DistanceTo(ip) <= distanceThreshold))
                .ToList();

            var samletPunkterForBeams = new List<Point3d>();
            samletPunkterForBeams.AddRange(gridForBeams.Concat(intersectionForBeams).Concat(cornerPointsForBeams));

            double snapTol = 1e-3; // 1 mm
            Point3d Snap(Point3d p)
            {
                return new Point3d(
                  Math.Round(p.X / snapTol) * snapTol,
                  Math.Round(p.Y / snapTol) * snapTol,
                  Math.Round(p.Z / snapTol) * snapTol
                );
            }

            samletPunkterForBeams = samletPunkterForBeams
  .Select(p => Snap(p))
  .ToList();
            samletPunkterForBeams = Point3d.CullDuplicates(samletPunkterForBeams, 0.001).ToList();

            var beams = GenerateBeamsFromPoints(samletPunkterForBeams);
            beams = FilterBeamsInsideOrOnBreps(beams, breps);

            building.Beams = beams; //Alle bjelker

            List<Beam> xBeams, yBeams;
            SplitBeamsByDirection(beams, out xBeams, out yBeams); //Splitter bjelker i x og y-retning
            SortBeamsByLengthDominance(xBeams, yBeams, out var primaryBeams, out var secondaryBeams); //Sorterer primary and secondary
            SplitPrimaryIntoMiddleAndEdgeByBoundingBox(primaryBeams, grupper, out var middleBeams, out var edgeBeams); //Finner edge og interalbeams. 

            //Setter opp BeamSublists:
            building.BeamSublists = new List<List<Beam>>
          {
              middleBeams, // Index 0 = Internal beams
              edgeBeams,   // Index 1 = Edge beams
              secondaryBeams // Index 2 = Secondary beams
          };


            //Søyler
            var columns = GenerateColumnsFromPoints(samletPunkter);
            building.Columns = columns;

            //Dekke
            var alleBreps = grupper.SelectMany(g => g).ToList();
            var slabs = SplitSlabsWithBeams(GenerateSlab(samletPunkterForBeams, alleBreps), beams);
            building.Slabs = slabs;



            double tol = 1e-6;
            var allBraces = new List<Line>();

            // 1) Group column endpoints by floor level (Z)
            var colsByLevel = columns
              .SelectMany(c => new[] { c.Axis.From, c.Axis.To })
              .GroupBy(p => Math.Round(p.Z / tol) * tol)
              .ToDictionary(
                g => g.Key,
                g => g.Distinct(new Point3dEqualityComparer(tol))
                      .OrderBy(p => p.X).ThenBy(p => p.Y).ToList()
              );

            var levels = colsByLevel.Keys.OrderBy(z => z).ToArray();

            // 2) For each story pair, X‐ and Y‐brace every bay
            for (int k = 0; k < levels.Length - 1; ++k)
            {
                double zLow = levels[k], zUp = levels[k + 1];
                var lowPts = colsByLevel[zLow];
                var upPts = colsByLevel[zUp];

              

                // 2A) X‐direction (rows of equal Y)
                foreach (var row in lowPts.GroupBy(p => Math.Round(p.Y / tol) * tol))
                {
                    double yKey = row.Key;
                    var rowLow = row.OrderBy(p => p.X).ToList();
                    var rowUp = upPts.Where(p => Math.Abs(p.Y - yKey) < tol)
                                      .OrderBy(p => p.X).ToList();
                    int n = Math.Min(rowLow.Count, rowUp.Count);
                    for (int i = 0; i < n - 1; ++i)
                    {
                        allBraces.Add(new Line(Snap(rowLow[i]), Snap(rowUp[i + 1])));
                        allBraces.Add(new Line(Snap(rowLow[i + 1]), Snap(rowUp[i])));
                    }
                }

                // 2B) Y‐direction (columns of equal X)
                foreach (var col in lowPts.GroupBy(p => Math.Round(p.X / tol) * tol))
                {
                    double xKey = col.Key;
                    var colLow = col.OrderBy(p => p.Y).ToList();
                    var colUp = upPts.Where(p => Math.Abs(p.X - xKey) < tol)
                                      .OrderBy(p => p.Y).ToList();
                    int n = Math.Min(colLow.Count, colUp.Count);
                    for (int i = 0; i < n - 1; ++i)
                    {
                        allBraces.Add(new Line(Snap(colLow[i]), Snap(colUp[i + 1])));
                        allBraces.Add(new Line(Snap(colLow[i + 1]), Snap(colUp[i])));
                    }
                }
            }
            // suppose you have:
            List<Brep> footprints = breps;

            // keep only those braces whose midpoint lies inside *any* footprint Brep
            var keptLines = allBraces
              .Where(l => {
                  // compute midpoint
                  var mid = new Point3d(
        0.5 * (l.From.X + l.To.X),
        0.5 * (l.From.Y + l.To.Y),
        0.5 * (l.From.Z + l.To.Z)
      );
                  // test midpoint against each Brep in the list
                  return footprints.Any(fp => fp.IsPointInside(mid, tol, false));
              })
              .ToList();

            // now wrap and assign
            building.Bracings = keptLines
              .Select(l => new Bracing(l))
              .ToList();
            /*
            // 3) Assign to your building
            building.Bracings = allBraces
              .Select(l => new Bracing(l))
              .ToList();


            */


            DA.SetData(0, building);
        }

        
        /// <summary>
        /// Simple comparer to remove duplicate Point3d with a tolerance.
        /// </summary>




        /// <summary>
        /// Simple comparer to CullDuplicates and Distinct on Point3d with tolerance.
        /// </summary>
        private class Point3dEqualityComparer : IEqualityComparer<Point3d>
        {
            private readonly double _tol;
            public Point3dEqualityComparer(double tol) => _tol = tol;
            public bool Equals(Point3d a, Point3d b) => a.DistanceTo(b) < _tol;
            public int GetHashCode(Point3d p)
              => p.X.GetHashCode() ^ p.Y.GetHashCode() ^ p.Z.GetHashCode();
        }
        public static List<List<Brep>> GroupIntersectingBreps(List<Brep> breps) //finner grupper med breper som intersecter
        {
            var groups = new List<List<Brep>>(); //Liste som skal inneholde grupper av Brep-objekter

            foreach (var brep in breps) //looper gjennom alle breper
            {
                BoundingBox bb1 = brep.GetBoundingBox(true); //finner boundingbox til brepene
                List<List<Brep>> overlappingGroups = new List<List<Brep>>();

                foreach (var group in groups) //finner breper som intersecter, og lager gruppe
                {
                    if (group.Any(b => BoundingBox.Intersection(bb1, b.GetBoundingBox(true)).IsValid))
                    {
                        overlappingGroups.Add(group);
                    }

                }

                if (overlappingGroups.Count == 0) //lager gruppe for breper som ikke intersecter
                {
                    groups.Add(new List<Brep> { brep });
                }
                else
                {
                    List<Brep> mergedGroup = new List<Brep> { brep };
                    foreach (var g in overlappingGroups)
                    {
                        mergedGroup.AddRange(g);
                        groups.Remove(g);
                    }
                    groups.Add(mergedGroup);
                }
            }
            return groups;
        }
        public static Point3d FindstartPointForGroup(List<Brep> group) //Finner startpunkt for hver brep-gruppe
        {
            List<(BrepFace face, BoundingBox bbox)> horisontaleFlater = new List<(BrepFace, BoundingBox)>(); //liste til horisontale flater

            // Finn alle horisontale flater i gruppen
            foreach (var brep in group)
            {
                foreach (var face in brep.Faces)
                {
                    if (!face.TryGetPlane(out Plane plane))
                        continue;

                    if (Math.Abs(plane.Normal.Z) < 0.99)
                        continue;

                    var bbox = face.GetBoundingBox(true);
                    horisontaleFlater.Add((face, bbox));
                }
            }

            if (horisontaleFlater.Count == 0)
                return Point3d.Origin;

            // Finn laveste Z-verdi
            double minZ = horisontaleFlater.Min(f => f.bbox.Min.Z);

            // Finn alle flater som ligger på denne Z-høyden (med liten toleranse)
            double tol = 0.001;
            var lavesteFlater = horisontaleFlater
                .Where(f => Math.Abs(f.bbox.Min.Z - minZ) < tol)
                .ToList();

            if (group.Count == 1 || lavesteFlater.Count == 1)
            {
                // Bare én brep i gruppa, eller bare én lavestliggende flate:
                var centroid = AreaMassProperties.Compute(lavesteFlater[0].face).Centroid;
                return centroid;
            }
            else
            {
                // Flere flater på samme laveste nivå – bruk hjørnet med minst X/Y
                Point3d hjørne = lavesteFlater
                    .Select(f => f.bbox.Min)
                    .OrderBy(p => p.X)
                    .ThenBy(p => p.Y)
                    .First();

                return hjørne;
            }

        }
        public static List<GridPoint> Generate3DGrid(
            List<Brep> gruppe,
            Point3d start,
            double xSpacing,
            double ySpacing,
            double floorHeight)
        {
            var points = new List<GridPoint>();
            BoundingBox bbox = BoundingBox.Empty;

            foreach (var brep in gruppe)
                bbox = BoundingBox.Union(bbox, brep.GetBoundingBox(true));

            int xStepsNeg = (int)Math.Ceiling((start.X - bbox.Min.X) / xSpacing);
            int xStepsPos = (int)Math.Ceiling((bbox.Max.X - start.X) / xSpacing);
            int yStepsNeg = (int)Math.Ceiling((start.Y - bbox.Min.Y) / ySpacing);
            int yStepsPos = (int)Math.Ceiling((bbox.Max.Y - start.Y) / ySpacing);
            int zSteps = (int)Math.Ceiling((bbox.Max.Z - start.Z) / floorHeight);

            for (int i = -xStepsNeg; i <= xStepsPos; i++)
            {
                for (int j = -yStepsNeg; j <= yStepsPos; j++)
                {
                    for (int k = 0; k <= zSteps; k++)
                    {
                        double x = start.X + i * xSpacing;
                        double y = start.Y + j * ySpacing;
                        double z = start.Z + k * floorHeight;

                        var pt = new Point3d(x, y, z);

                        if (IsPointInsideOrOnSurface(pt, gruppe, 0.1))
                            points.Add(new GridPoint(i, j, k, pt));
                    }
                }
            }

            return points;
        }
        public static List<Line> CreateLinesThroughGridPoints(List<GridPoint> points, double extraLength = 10)
        {
            var lines = new List<Line>();

            // Grupper punktene etter Z (nivåer)
            var levels = points.GroupBy(p => p.K);

            foreach (var level in levels)
            {
                var pointsOnLevel = level.ToList();

                // Lag linjer i X-retning (samme Y-verdi)
                var yGroups = pointsOnLevel.GroupBy(p => p.J);
                foreach (var group in yGroups)
                {
                    var sortedX = group.OrderBy(p => p.I).Select(p => p.Point).ToList();
                    var line = new Line(
                        sortedX.First() - new Vector3d(extraLength, 0, 0),
                        sortedX.Last() + new Vector3d(extraLength, 0, 0)
                    );
                    lines.Add(line);
                }

                // Lag linjer i Y-retning (samme X-verdi)
                var xGroups = pointsOnLevel.GroupBy(p => p.I);
                foreach (var group in xGroups)
                {
                    var sortedY = group.OrderBy(p => p.J).Select(p => p.Point).ToList();
                    var line = new Line(
                        sortedY.First() - new Vector3d(0, extraLength, 0),
                        sortedY.Last() + new Vector3d(0, extraLength, 0)
                    );
                    lines.Add(line);
                }
            }

            return lines;
        }
        public static List<Point3d> FindIntersectionsWithBrep(List<Line> lines, List<Brep> brepGruppe, double tolerance = 0.01)
        {
            var intersections = new List<Point3d>();

            foreach (var line in lines)
            {
                foreach (var brep in brepGruppe)
                {
                    Curve[] overlapCurves;
                    Point3d[] intersectionPoints;

                    bool success = Rhino.Geometry.Intersect.Intersection.CurveBrep(
                        line.ToNurbsCurve(),
                        brep,
                        tolerance,
                        out overlapCurves,
                        out intersectionPoints
                    );

                    if (success)
                    {
                        // Legg til vanlige intersection points
                        foreach (var pt in intersectionPoints)
                        {
                            intersections.Add(pt);
                        }

                        // Behandle overlapCurves
                        if (overlapCurves != null)
                        {
                            foreach (var overlapCurve in overlapCurves)
                            {
                                // Legg til start- og sluttpunkt av overlappende kurver
                                intersections.Add(overlapCurve.PointAtStart);
                                intersections.Add(overlapCurve.PointAtEnd);


                            }
                        }
                    }
                }
            }

            // Optional: Fjern duplikate punkter som oppstår
            intersections = Point3d.CullDuplicates(intersections, tolerance).ToList();

            return intersections;
        }
        public static List<Point3d> GenerateCornerPointsPerBrep(List<Brep> gruppe, double floorHeight)
        {
            var cornerPoints = new List<Point3d>();

            foreach (var brep in gruppe)
            {
                var bbox = brep.GetBoundingBox(true);

                // Hjørner på grunnplanet
                var baseCorners = new List<Point3d>
{
    new Point3d(bbox.Min.X, bbox.Min.Y, bbox.Min.Z),
    new Point3d(bbox.Max.X, bbox.Min.Y, bbox.Min.Z),
    new Point3d(bbox.Min.X, bbox.Max.Y, bbox.Min.Z),
    new Point3d(bbox.Max.X, bbox.Max.Y, bbox.Min.Z)
};

                // Antall nivåer i høyden
                int zSteps = (int)Math.Ceiling((bbox.Max.Z - bbox.Min.Z) / floorHeight);

                // Lag punkter i høyden for hvert hjørne
                for (int k = 0; k <= zSteps; k++)
                {
                    double z = bbox.Min.Z + k * floorHeight;

                    foreach (var basePt in baseCorners)
                    {
                        var pt = new Point3d(basePt.X, basePt.Y, z);

                        if (IsPointInsideOrOnSurface(pt, new List<Brep> { brep }, 0.1))
                            cornerPoints.Add(pt);
                    }
                }
            }

            return cornerPoints;
        }
    

            
            
        
        public static bool IsPointInsideOrOnSurface(Point3d pt, List<Brep> brepGruppe, double tolerance = 0.1)
        {
            foreach (var brep in brepGruppe)
            {
                if (brep.IsPointInside(pt, tolerance, true))
                    return true;

                foreach (var face in brep.Faces)
                {
                    if (face.ClosestPoint(pt, out double u, out double v))
                    {
                        // Sjekk at punktet er innenfor trimmed område
                        var relation = face.IsPointOnFace(u, v);
                        if (relation != PointFaceRelation.Exterior)
                        {
                            Point3d projected = face.PointAt(u, v);
                            if (pt.DistanceTo(projected) < tolerance)
                                return true;
                        }
                    }
                }
            }

            return false;
        }
        public static List<Beam> GenerateBeamsFromPoints(List<Point3d> points, double tolerance = 0.001) //For flere breper
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
        public static List<Beam> FilterBeamsInsideOrOnBreps(List<Beam> beams, List<Brep> breps, double tolerance = 0.01)
        {
            return beams
                .Where(beam =>
                {
                    Point3d midpoint = beam.Axis.PointAt(0.5);
                    return breps.Any(brep =>
                        brep.IsPointInside(midpoint, tolerance, true) ||
                        IsPointOnAnySurface(midpoint, brep, tolerance)
                    );
                })
                .ToList();
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
        public static void SplitPrimaryIntoMiddleAndEdgeByBoundingBox(
            List<Beam> primaryBeams,
            List<List<Brep>> grupper,
            out List<Beam> middleBeams,
            out List<Beam> edgeBeams,
            double edgeTolerance = 0.1)
        {
            middleBeams = new List<Beam>();
            edgeBeams = new List<Beam>();

            if (primaryBeams.Count == 0 || grupper.Count == 0)
                return;

            // Finn primary retning:
            Vector3d primaryDir = primaryBeams[0].Axis.Direction;
            primaryDir.Unitize();
            bool isXPrimary = Math.Abs(primaryDir.X) > Math.Abs(primaryDir.Y);

            // ✅ Her lagrer vi hvor mange ganger en bjelke har blitt klassifisert som edge
            Dictionary<Beam, int> edgeCount = primaryBeams.ToDictionary(b => b, b => 0);

            foreach (var gruppe in grupper)
            {
                foreach (var brep in gruppe)
                {
                    var bbox = brep.GetBoundingBox(true);
                    double minCoord = isXPrimary ? bbox.Min.Y : bbox.Min.X;
                    double maxCoord = isXPrimary ? bbox.Max.Y : bbox.Max.X;

                    var beamsInBrep = primaryBeams.Where(b =>
                    {
                        var midPt = b.Axis.PointAt(0.5);
                        return brep.IsPointInside(midPt, 0.01, true) ||
                               IsPointOnAnySurface(midPt, brep, 0.1);
                    }).ToList();

                    foreach (var beam in beamsInBrep)
                    {
                        double fromCoord = isXPrimary ? beam.Axis.From.Y : beam.Axis.From.X;
                        double toCoord = isXPrimary ? beam.Axis.To.Y : beam.Axis.To.X;

                        bool isEdge = Math.Abs(fromCoord - minCoord) < edgeTolerance ||
                                      Math.Abs(fromCoord - maxCoord) < edgeTolerance ||
                                      Math.Abs(toCoord - minCoord) < edgeTolerance ||
                                      Math.Abs(toCoord - maxCoord) < edgeTolerance;

                        if (isEdge)
                            edgeCount[beam] += 1;  // Øk edge-telleren for denne bjelken!
                    }
                }
            }

            // ✅ Etterpå, sorter bjelkene basert på hvor mange ganger de ble klassifisert som edge
            foreach (var kvp in edgeCount)
            {
                if (kvp.Value == 1)
                    edgeBeams.Add(kvp.Key);           // Edge i kun én brep → behold som edge
                else
                    middleBeams.Add(kvp.Key);         // Edge i flere breper → sett som middle
            }
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
        public static List<Slab> GenerateSlab(List<Point3d> points, List<Brep> breps, double defaultThickness = 0.2) //For flere breps
        {
            HashSet<double> levels = new HashSet<double>();
            foreach (var p in points)
                levels.Add(Math.Round(p.Z, 3));

            if (levels.Count > 0)
                levels.Remove(levels.Min());

            List<Plane> planes = levels
                .Select(z => new Plane(new Point3d(0, 0, z), Vector3d.ZAxis))
                .ToList();

            List<Slab> slabs = new List<Slab>();

            foreach (Plane pl in planes)
            {
                List<Curve> allCurvesAtLevel = new List<Curve>();

                foreach (var brep in breps)
                {
                    Curve[] intersectionCurves;
                    Point3d[] _;
                    Rhino.Geometry.Intersect.Intersection.BrepPlane(brep, pl, 0.001, out intersectionCurves, out _);

                    if (intersectionCurves != null)
                        allCurvesAtLevel.AddRange(intersectionCurves);
                }

                if (allCurvesAtLevel.Count == 0)
                    continue;

                // Først join: kurver som henger sammen
                Curve[] joined = Curve.JoinCurves(allCurvesAtLevel, 0.001);

                // Så: slå sammen overlappende regioner
                Curve[] unioned = Curve.CreateBooleanUnion(joined, 0.001);

                if (unioned == null) continue;

                foreach (var crv in unioned)
                {
                    if (crv.IsClosed && crv.IsPlanar())
                    {
                        var brepSlabs = Brep.CreatePlanarBreps(crv, 0.001);
                        if (brepSlabs != null && brepSlabs.Length > 0)
                        {
                            foreach (var brepSlab in brepSlabs)
                            {
                                slabs.Add(new Slab(brepSlab, defaultThickness, pl.OriginZ));
                            }
                        }
                    }
                }
            }

            return slabs;
        }

        public static List<Slab> SplitSlabsWithBeams(List<Slab> slabs, List<Beam> beams)
        {
            List<Slab> result = new List<Slab>();

            foreach (var slab in slabs)
            {
                Brep brep = slab.Geometry;

                BrepFace face = brep.Faces[0];
                Surface surface = face.UnderlyingSurface();

                Plane slabPlane;
                if (!surface.TryGetPlane(out slabPlane))
                {
                    result.Add(slab); // fallback: behold original slab
                    continue;
                }

                List<Curve> projectedCurves = new List<Curve>();

                foreach (var beam in beams)
                {
                    Line axis = beam.Axis;
                    if (Math.Abs(axis.From.Z - slabPlane.OriginZ) < 0.01 &&
                        Math.Abs(axis.To.Z - slabPlane.OriginZ) < 0.01)
                    {
                        LineCurve beamCurve = new LineCurve(axis);
                        Curve projected = Curve.ProjectToPlane(beamCurve, slabPlane);
                        projectedCurves.Add(projected);
                    }
                }

                if (projectedCurves.Count == 0)
                {
                    result.Add(slab);
                }
                else
                {
                    Brep[] split = brep.Split(projectedCurves, 0.001);
                    if (split != null && split.Length > 0)
                    {
                        foreach (var sub in split)
                        {
                            result.Add(new Slab(sub, slab.Thickness, slab.Level));
                        }
                    }
                    else
                    {
                        result.Add(slab);
                    }
                }
            }

            return result;
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
            get { return new Guid("8096FBC6-5283-4DC2-9DE6-C88D37893798"); }
        }
    }
}