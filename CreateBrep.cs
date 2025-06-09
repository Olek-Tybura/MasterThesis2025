using System;
using System.Collections.Generic;

using Grasshopper.Kernel;
using Rhino.Geometry;

namespace MasterThesisV2
{
    public class CreateBrep : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the CreateBrep class.
        /// </summary>
        public CreateBrep()
          : base("CreateBrep", "Nickname",
              "Description",
              "MasterThesis", "V4")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddPointParameter("Starting Point", "", "", GH_ParamAccess.item, new Point3d(0, 0, 0));
            pManager.AddNumberParameter("X size", "", "", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Y size", "", "", GH_ParamAccess.item, 10);
            pManager.AddNumberParameter("Z size", "", "", GH_ParamAccess.item, 10);
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddBrepParameter("Brep", "", "", GH_ParamAccess.item);
        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            Point3d corner = Point3d.Origin; // Default til (0,0,0)
            double x = 10.0, y = 10.0, z = 10.0;

            // Bare overskriv default hvis brukeren har plugget inn noe
            if (!DA.GetData(0, ref corner)) return;
            if (!DA.GetData(1, ref x)) return;
            if (!DA.GetData(2, ref y)) return;
            if (!DA.GetData(3, ref z)) return;

            Interval xInterval = new Interval(0, x);
            Interval yInterval = new Interval(0, y);
            Interval zInterval = new Interval(0, z);

            Plane basePlane = new Plane(corner, Vector3d.XAxis, Vector3d.YAxis);
            Box box = new Box(basePlane, xInterval, yInterval, zInterval);

            Brep brep = box.ToBrep();
            DA.SetData(0, brep);
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
            get { return new Guid("FFBE3917-A366-4609-A279-865B59EEDD86"); }
        }
    }
}