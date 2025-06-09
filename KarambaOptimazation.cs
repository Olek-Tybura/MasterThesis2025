using System;
using System.Collections.Generic;
using Grasshopper.Kernel;
using Rhino.Geometry;
using System.Linq;
using Grasshopper.Kernel.Types;
using Grasshopper.Kernel.Data;
using Karamba.Utilities;
using Karamba.Results;
using Karamba.Materials;
using Karamba.Geometry;
using Karamba.CrossSections;
using Karamba.Supports;
using Karamba.Loads;
using Karamba.Models;
using Karamba.GHopper.Models;
using Karamba.GHopper.Utilities.Mesher;
using static Rhino.DocObjects.PhysicallyBasedMaterial;
using Karamba.GHopper.Geometry;
//using Microsoft.VisualBasic.Logging;
using static Rhino.Render.CustomRenderMeshes.RenderMeshProvider;
using Karamba.Joints;
using Karamba.Algorithms;

using System.Diagnostics;
using Karamba.Elements;


using Karamba.Factories;

using UnitsNet;
using Master_Thesis;




namespace MasterThesisV2
{
    public class KarambaOptimazation : GH_Component
    {
        /// <summary>
        /// Initializes a new instance of the KarambaOptimazation class.
        /// </summary>
        public KarambaOptimazation()
          : base("KarambaOptimazation", "Nickname",
              "Description",
              "MasterThesis", "V4")
        {
        }

        /// <summary>
        /// Registers all the input parameters for this component.
        /// </summary>
        protected override void RegisterInputParams(GH_Component.GH_InputParamManager pManager)
        {
            pManager.AddLineParameter("Columns", "cols", "Columns of the building", GH_ParamAccess.list);
            

            pManager.AddLineParameter("InternalBeams", "ibms", "Internal beams of the building", GH_ParamAccess.list, new Line());
            this.Params.Input[1].Optional = true;

            pManager.AddLineParameter("ExternalBeams", "ebms", "External beams of the building", GH_ParamAccess.list,new Line());
            this.Params.Input[2].Optional = true;

            pManager.AddLineParameter("SecondaryBeams", "sbms", "Secondary beams of the building", GH_ParamAccess.list);
            pManager.AddMeshParameter("Mesh", "m", "", GH_ParamAccess.list);

            pManager.AddNumberParameter("LoadValue", "LV", "Uniform load magnitude (kN/m)", GH_ParamAccess.item, 2.0);
            pManager.AddBrepParameter("Slabs", "sbs", "Slabs breps of the building", GH_ParamAccess.list);
         

            pManager.AddNumberParameter("BuildingType", "BT", "Number of the building type you want to use.", GH_ParamAccess.item);
        
        }

        /// <summary>
        /// Registers all the output parameters for this component.
        /// </summary>
        protected override void RegisterOutputParams(GH_Component.GH_OutputParamManager pManager)
        {
            pManager.AddGenericParameter("Model", "M", "Assembled and analyzed Karamba model", GH_ParamAccess.item);
            pManager.AddNumberParameter("MaxDisp", "d", "Maximum displacement (m)", GH_ParamAccess.item);
            //pManager.AddPointParameter("Point", "p", "Inclusionpoints for the meshload", GH_ParamAccess.list);
            //pManager.AddMeshParameter("Mesh", "s", "Support points", GH_ParamAccess.list);
            //pManager.AddTextParameter("ID", "s", "Support points", GH_ParamAccess.list);
            //pManager.AddLineParameter("Segments", "S", "segments", GH_ParamAccess.list);
            //pManager.AddPointParameter("Divpoints", "p", "Inclusionpoints for the meshload", GH_ParamAccess.list);
            //pManager.AddSurfaceParameter("Breps", "BNrep", "Brep", GH_ParamAccess.list);


        }

        /// <summary>
        /// This is the method that actually does the work.
        /// </summary>
        /// <param name="DA">The DA object is used to retrieve from inputs and store in outputs.</param>
        protected override void SolveInstance(IGH_DataAccess DA)
        {
            List<Line> internalBeams = new List<Line>();
            DA.GetDataList<Line>(1, internalBeams);


            var externalBeams = new List<Line>();
            DA.GetDataList(2, externalBeams);

            List<Line> secondaryBeams = new List<Line>();
            if (!DA.GetDataList<Line>(3, secondaryBeams))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve secondary beams.");
                return;
            }
            List<Line> cols = new List<Line>();
            if (!DA.GetDataList<Line>(0, cols))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve columns.");
                return;
            }
            List<Brep> slabs = new List<Brep>();
            if (!DA.GetDataList<Brep>(6, slabs))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve slabs.");
                return;
            }

            double loadValue = 0.0;
            if (!DA.GetData(5, ref loadValue))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve load value.");
                return;
            }

            List<Mesh> mesh = new List<Mesh>();
            if (!DA.GetDataList<Mesh>(4, mesh))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve mesh.");
                return;
            }

            double buldingType = 1;
            if (!DA.GetData(7, ref buldingType))
            {
                AddRuntimeMessage(GH_RuntimeMessageLevel.Error, "Failed to retrieve building type.");
                return;
            }

          






            // Initialize Karamba toolkit and logger
            KarambaCommon.Toolkit k3d = new KarambaCommon.Toolkit();
            MessageLogger logger = new MessageLogger();
            FactoryLoad loadFactory = new FactoryLoad();
            var factoryCrossSection = new Karamba.Factories.FactoryCrossSection();

            List<Karamba.GHopper.Elements.GH_Element> ghElems = new List<Karamba.GHopper.Elements.GH_Element>();

            //Creating the materials for the model 
            FemMaterial_Orthotropic strongBothDirT22 = CreateT22StrongBothDir();
            FemMaterial_Orthotropic T22 = CreateT22ConiferousTimber();
            FemMaterial_Orthotropic T22_StrongOtherDir = CreateT22_StrongOtherDirection();

          

            FemMaterial s355Steel = CreateS355SteelMaterial();
            FemMaterial_Orthotropic ConcreteB30 = CreateC30_37Concrete();
            FemMaterial_Orthotropic ConcreteB35 = CreateC35_45Concrete();
            FemMaterial_Orthotropic ConcreteB40 = CreateC40_50Concrete();
            FemMaterial_Orthotropic GL32c = Create32c_GulamTimber();
           
           
           
            //gENEREATING CROSS-SECTIONS FOR THE BEAMS AND SHEELS 


            List<CroSec_I> HEAProfiles = GenerateHEAProfiles(s355Steel);

            List<CroSec_Trapezoid> GulamBeams = CreateGlulamCrossSections(GL32c);
            
            List<CroSec_Trapezoid> ConcreteBeams = CreateProfilesInSituConcrete(ConcreteB35, 30.0, 100.0);
            List<CroSec_Box> hsq_profiles = GenerateBoxCrossSections(s355Steel);
            List<CroSec_Box> fsq_profiles = GenerateBoxCrossSectionsFSQ(s355Steel);


            
            List<CroSec_Shell> ConcreteSlabs = CreateConcreteShellCrossSections(ConcreteB30, 10.0, 35.0, 2.5);

            List<CroSec_Shell> TimberShells = CreateTimberShells(strongBothDirT22);


            double slablength = secondaryBeams[0].Length;
            (double Height, double SelfWeight) = GetHollowCoreProfiles(slablength);
            double neededHeight = Height;
            double slabWeight = SelfWeight;
            CroSec_Shell HollowCoreSlabs = factoryCrossSection.ShellConst(neededHeight / 100, 0.0, ConcreteB35, "ConcreteSlabs", "HollowCoreSlabs", "EU", System.Drawing.Color.Gray);





           












            //Converting the Rhino lines to Karamba lines


            List<string> beamIDs;
            List<Karamba.Geometry.Line3> intElements;
            List<Karamba.Geometry.Line3> extElements;
            List<Karamba.Geometry.Line3> secElements;
            List<Karamba.Geometry.Line3> colElements;
          

            CreateKarambaBeams(internalBeams, "beam", out intElements, out beamIDs);
            CreateKarambaBeams(externalBeams, "beam", out extElements, out beamIDs);
            CreateKarambaBeams(secondaryBeams, "beam", out secElements, out beamIDs);
            CreateKarambaBeams(cols, "beam", out colElements, out beamIDs);
          



            //CREATING THE BEAM ELEMENTS

            List<CroSec> selectedCrossSections;
            List<CroSec> selectedCrossSectionsExt = new List<CroSec>();
            List<CroSec> selectedCrossSectionsSec = new List<CroSec>();
            List<CroSec> selectedShellCrossSections;

            if (buldingType == 1) // Steel
            {
                selectedCrossSections = hsq_profiles.Cast<CroSec>().ToList();
                selectedCrossSectionsExt = fsq_profiles.Cast<CroSec>().ToList();
                selectedCrossSectionsSec = HEAProfiles.Cast<CroSec>().ToList();
                selectedShellCrossSections = new List<CroSec> { HollowCoreSlabs }; // Steel buildings use hollow core slabs
            }
            else if (buldingType == 2) // Timber
            {
                
                selectedCrossSections = GulamBeams.Cast<CroSec>().ToList();
                selectedShellCrossSections = TimberShells.Cast<CroSec>().ToList();
            }
            else // Concrete
            {
                selectedCrossSections = ConcreteBeams.Cast<CroSec>().ToList();
                selectedShellCrossSections = ConcreteSlabs.Cast<CroSec>().ToList();
            }

            CroSec initialCroSec1;
            CroSec initialExt1;
            CroSec initialSec1;
            CroSec initialShell1;

            if (buldingType == 1 || buldingType == 2)
            {
                initialCroSec1 = selectedCrossSections[22];
                initialExt1 = selectedCrossSectionsExt.Count > 0 ? selectedCrossSectionsExt[22] : null;
                initialSec1 = selectedCrossSectionsSec.Count > 0 ? selectedCrossSectionsSec[22] : null;
                initialShell1 = selectedShellCrossSections[0];
            }
            else
            {
                initialCroSec1 = selectedCrossSections[200];
                initialExt1 = selectedCrossSectionsExt.Count > 0 ? selectedCrossSectionsExt[200] : null;
                initialSec1 = selectedCrossSectionsSec.Count > 0 ? selectedCrossSectionsSec[50] : null;
                initialShell1 = selectedShellCrossSections[0];
            }

            CroSec initialCroSec = initialCroSec1;
            CroSec initialExt = initialExt1;
            CroSec initialSec = initialSec1;
            CroSec initialShell = initialShell1;

















            List<string> internalBeamsID = new List<string>();
            for (int i = 0; i < intElements.Count; i++)
            {
                internalBeamsID.Add("InternalBeams" + i);
            }

            List<CroSec> IntInitialCroSec = Enumerable.Repeat((CroSec)initialCroSec, intElements.Count).ToList();

            List<Point3> internalNodes = new List<Point3>();                        //Creating the BeamElements for the internal beams.
            var internalElms = k3d.Part.LineToBeam(intElements,
                internalBeamsID,
                IntInitialCroSec,
                logger,
                out internalNodes);
            foreach (var b in internalElms)
            {
                ghElems.Add(new Karamba.GHopper.Elements.GH_Element(b));
            }



            List<string> edgeBeamsID = new List<string>();                          // Loop for creating the elementIDs for the edge elements
            for (int i = 0; i < extElements.Count; i++)
            {
                edgeBeamsID.Add("edgeBeams" + i);
            }


            List<CroSec> ExtInitialCroSec;

            if (buldingType == 1)
            {
                ExtInitialCroSec = Enumerable.Repeat((CroSec)initialExt, extElements.Count).ToList();
            }
            else
            {
                ExtInitialCroSec = Enumerable.Repeat((CroSec)initialCroSec, extElements.Count).ToList(); // ✅ just assign
            }


            List<Point3> edgeNodes = new List<Point3>();
            var edgeElms = k3d.Part.LineToBeam(extElements,                           //Creating the BeamElements for the edge beams.
                edgeBeamsID,
                ExtInitialCroSec,
                logger,
                out edgeNodes);
            foreach (var b in edgeElms)
            {
                ghElems.Add(new Karamba.GHopper.Elements.GH_Element(b));
            }

            List<string> secBeamsID = new List<string>();                                       // Loop for creating the elementIDs for the secondary elements
            for (int i = 0; i < secElements.Count; i++)
            {
                secBeamsID.Add("secBeams" + i);
            }



            List<CroSec> SecInitialCroSec;

            if (buldingType == 1)
            {
                SecInitialCroSec = Enumerable.Repeat((CroSec)initialSec, secElements.Count).ToList();
            }
            else
            {
                SecInitialCroSec = Enumerable.Repeat((CroSec)initialCroSec, secElements.Count).ToList();
            }

            List<Point3> secNodes = new List<Point3>();
            var secElms = k3d.Part.LineToBeam(secElements,                                         //Creating the BeamElements for the secondary beams.
                secBeamsID,
                SecInitialCroSec,
                logger,
                out secNodes);

            foreach (var b in secElms)
            {
                ghElems.Add(new Karamba.GHopper.Elements.GH_Element(b));
            }
            
            

            List<string> colsID = new List<string>();                                       // Loop for creating the elementIDs for the column elements
            for (int i = 0; i < colElements.Count; i++)
            {
                colsID.Add("columns" + i);
            }
            
            List<CroSec> ColInitialCroSec;

            if (buldingType == 1)
            {
                ColInitialCroSec = Enumerable.Repeat((CroSec)initialSec, colElements.Count).ToList();
            }
            else
            {
                ColInitialCroSec = Enumerable.Repeat((CroSec)initialCroSec, colElements.Count).ToList();
            }
            
            List<Point3> colNodes = new List<Point3>();                                 //Creating the BeamElements for the columns.
            var colElms = k3d.Part.LineToBeam(colElements,
                colsID,
                ColInitialCroSec,
                logger,
                out colNodes);
            foreach (var b in colElms)
            {
                ghElems.Add(new Karamba.GHopper.Elements.GH_Element(b));
            }
           
            
            
            






            //Converting the slab mesh 

            List<Mesh3> meshedBreps3 = mesh.Select(m => m.Convert()).ToList();



            List<Point3> surfaceNodes = new List<Point3>();
            List<string> slabID = new List<string>();                                               // Loop for creating the elementIDs for the slab elements
            for (int i = 0; i < meshedBreps3.Count; i++)
            {
                slabID.Add("Slabs" + i);
            }


            List<CroSec> shellCrossSections = Enumerable.Repeat((CroSec)initialShell, meshedBreps3.Count).ToList();
            var slabElms = k3d.Part.MeshToShell(meshedBreps3,                                   //Creating the ShellElements for the slabs.
                slabID,
                shellCrossSections,
                logger,
                out surfaceNodes);


            


            if (buldingType == 2 || buldingType == 3)
            {
                foreach (var b in slabElms)
                {
                    ghElems.Add(new Karamba.GHopper.Elements.GH_Element(b));
                }
            }

         


            List<string> allBeamsID = new List<string>();                          // Loop for creating the elementIDs for the all beams
            allBeamsID.AddRange(internalBeamsID);
            allBeamsID.AddRange(edgeBeamsID);
            allBeamsID.AddRange(secBeamsID);


            List<Guid> emptyGuids = new List<Guid>();
            List<int> emptyNodeIndices = new List<int>();
         
            
            //Joints 
            double cx = 100;
            JointAgent beamjoint = new JointAgent(new double?[] {
    1e10, 1e10, 1e10,   // Tx, Ty, Tz: fixed
    0, 0, 0,   // Rx, Ry (hinged), Rz
    0, 0, 0,   // optional: alpha values ignored
    0, 0, 0
}, allBeamsID, emptyGuids, colsID, new List<int> { }, null);

          

            



            //ELEMENT SETS

            ElemSet internalElemSet = new ElemSet("InternalBeamsSet");
            foreach (string beamID in internalBeamsID)
            {
                internalElemSet.add(beamID);
            }

            ElemSet externalElemSet = new ElemSet("EdgeBeamsSet");
            foreach (string beamID in edgeBeamsID)
            {
                externalElemSet.add(beamID);
            }

            ElemSet secBeamElmSet = new ElemSet("SecBeamsSet");
            foreach (string beamID in secBeamsID)
            {
                secBeamElmSet.add(beamID);
            }
            
            ElemSet colBeamElmSet = new ElemSet("ColBeamsSet");
            foreach (string beamID in colsID)
            {
                colBeamElmSet.add(beamID);
            }

            
            ElemSet slabELmSet = new ElemSet("SlabsSet");
            foreach (var slab in slabID)
            {
                slabELmSet.add(slab);
            }

            





            //SUPPORTS



            //Columns

            List<Support> supports = CreateSupports(cols, k3d);

            

            // Combine the two lists into a single list
            //  var allSupports = supports.Concat(topSupports).ToList();


            //LOADS

            List<Load> AllLoads = new List<Load>();



            // Always add gravity
            GravityLoad gravityLoad = loadFactory.GravityLoad(new Vector3(0, 0, -1), "LC0");
            AllLoads.Add(gravityLoad);

            if (buldingType == 1) // Steel / HEA → Line loads
            { 

                List<string> combinedBT1IDs = new List<string>();
                combinedBT1IDs.AddRange(internalBeamsID);
                combinedBT1IDs.AddRange(edgeBeamsID);


                double loadBT1 = slabWeight + loadValue;

                var meshUnitLoadsCache = new Dictionary<MeshUnitLoad, MeshUnitLoad>();
                List<Load> meshLoads = new List<Load>();

                foreach (Mesh3 shellMesh in meshedBreps3)
                 {
                    var meshLoad = k3d.Load.MeshLoad(
                        new List<Vector3>() { new Vector3(0, 0, -loadBT1) },
                        shellMesh,
                        LoadOrientation.global,
                        false,                       // Not projected as area load
                        true,                        // Projected as line load onto beams
                        surfaceNodes,
                        combinedBT1IDs,
                        meshUnitLoadsCache,
                        "LC0"
                    );

                    meshLoads.Add(meshLoad);
                 }
                AllLoads.AddRange(meshLoads);

            }

                
            
                else if (buldingType == 2 || buldingType ==3) 
                {
                    List<string> combinedIDs = new List<string>();
                    combinedIDs.AddRange(internalBeamsID);
                    combinedIDs.AddRange(edgeBeamsID);
                combinedIDs.AddRange(secBeamsID);
                    

                    var meshUnitLoadsCache = new Dictionary<MeshUnitLoad, MeshUnitLoad>();
                    List<Load> meshLoads = new List<Load>();

                    foreach (Mesh3 shellMesh in meshedBreps3)
                    {
                        var meshLoad = k3d.Load.MeshLoad(
                            new List<Vector3>() { new Vector3(0, 0, -loadValue) },
                            shellMesh,
                            LoadOrientation.global,
                            false,                       // Not projected as area load
                            true,                        // Projected as line load onto beams
                            surfaceNodes,
                            combinedIDs,
                            meshUnitLoadsCache,
                            "LC0"
                        );

                        meshLoads.Add(meshLoad);
                    }

                    AllLoads.AddRange(meshLoads);
                }

            var allJoints = new List<Joint>();
           allJoints.Add(beamjoint);


            //ASSEMBLE MODEL

            var builderElemsNew = ghElems.Select(e => e.Value).ToList();
                double mass1;
                Point3 cog1;
                bool flag1;
                string info1;
                string msg1;
                var modelNew = k3d.Model.AssembleModel(
                    builderElemsNew,
                    supports,
                    AllLoads,
                    out info1,
                    out mass1,
                    out cog1,
                    out msg1,
                    out flag1, allJoints, null, new List<ElemSet> { internalElemSet, externalElemSet, colBeamElmSet, secBeamElmSet, slabELmSet }
                );

      
       

            //ANALYSE THE MODEL 

            IReadOnlyList<Vector3> out_forceNew;
                IReadOnlyList<double> out_energyNew;
                IReadOnlyList<double> max_dispNew;
                string warningNew;
                modelNew = k3d.Algorithms.Analyze(
                    modelNew,
                    new List<string>() { "LC0" },
                    out max_dispNew,
                    out out_forceNew,
                    out out_energyNew,
                    out warningNew
                );
          

         

            List<string> elemGrplds = new List<string> { "InternalBeamsSet", "EdgeBeamsSet", "SecBeamsSet", "ColBeamsSet" };
            List<string> colGrplds = new List<string> { "ColBeamsSet" };
            List<string> colAndSecGrplds = new List<string> { "SecBeamsSet" , "ColBeamsSet" };
            List<string> slabGrplds = new List<string> { "SlabsSet" };

            List<string> combinedIDsNewProfiles1 = new List<string>();
            combinedIDsNewProfiles1.AddRange(secBeamsID);
            combinedIDsNewProfiles1.AddRange(internalBeamsID);
           combinedIDsNewProfiles1.AddRange(edgeBeamsID);
           
           combinedIDsNewProfiles1.AddRange(colsID);

            List<Vector3> targetDeformDirs = new List<Vector3>();
            List<Vector3> targetDeformPlanes = new List<Vector3>();


            double UtzTarget = 0.7;
            double targetedDisp = 3.0;
            double targetedDispTol = 1.1;
            double targetVirtualWork = 300.8;
            double targetVirtualWorkSlabs = 300.8;
            Model outModel = null;
            Model outModel2 = null;
            Model outModel1 = null;
           
            Model outModel3 = null; // Initialize to null
        


            IReadOnlyList<string> unvonvergedCSOoptiElms;
            IReadOnlyList<string> innsufficientCSOptiElemsULS;
            IReadOnlyList<string> innsufficientCSOptiElemsSLS;
            IReadOnlyList<double> compliances;
            IReadOnlyList<double> maxDisplacements;
            string message;






            if (buldingType == 1) // STEEL — optimize beams in 3 passes
            {

               
                

                // 1. Internal Beams
                Karamba.Algorithms.OptiCroSec.solve(
                    modelNew, 2, true,
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    10, 10, 5,
                    targetedDisp, targetedDispTol,
                    targetDeformDirs, targetDeformPlanes,
                    UtzTarget, targetVirtualWork,
                    internalBeamsID,
                    new List<string> { "InternalBeamsSet" },
                    selectedCrossSections,
                    1.0, 1.05, true,
                    out maxDisplacements, out compliances, out message,
                    out innsufficientCSOptiElemsSLS, out innsufficientCSOptiElemsULS, out unvonvergedCSOoptiElms,
                    out outModel1
                );
               
                               
                // 2. External Beams
                Karamba.Algorithms.OptiCroSec.solve(
                    outModel1, 2, true,
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    10, 10, 5,
                    targetedDisp, targetedDispTol,
                    targetDeformDirs, targetDeformPlanes,
                    UtzTarget, targetVirtualWork,
                    edgeBeamsID,
                    new List<string> { "EdgeBeamsSet" },
                    selectedCrossSectionsExt,
                    1.0, 1.05, true,
                    out maxDisplacements, out compliances, out message,
                    out innsufficientCSOptiElemsSLS, out innsufficientCSOptiElemsULS, out unvonvergedCSOoptiElms,
                    out outModel2
                );
                
                // 3. Columns and Sec Beams 
                List<string> secAndColIDs = new List<string>();
                secAndColIDs.AddRange(secBeamsID);

               secAndColIDs.AddRange(colsID);

                Karamba.Algorithms.OptiCroSec.solve(
                    outModel2, 2, true,
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    new List<string> { "LC0" },
                    10, 10, 5,
                    targetedDisp, targetedDispTol,
                    targetDeformDirs, targetDeformPlanes,
                    UtzTarget, targetVirtualWork,
                    secAndColIDs,
                    colAndSecGrplds,
                    selectedCrossSectionsSec,
                    1.0, 1.05, true,
                    out maxDisplacements, out compliances, out message,
                    out innsufficientCSOptiElemsSLS, out innsufficientCSOptiElemsULS, out unvonvergedCSOoptiElms,
                    out outModel3
                );

                
            }
            else if (buldingType == 2 || buldingType == 3) // Timber or Concrete
            {
                


                

                Karamba.Algorithms.OptiCroSec.solve(modelNew, 2, true,
                    new List<string> { "LC0" }, new List<string> { "LC0" }, new List<string> { "LC0" },
                    10, 10, 5,
                    targetedDisp, targetedDispTol,
                    targetDeformDirs, targetDeformPlanes,
                   UtzTarget, targetVirtualWork,
                    combinedIDsNewProfiles1, elemGrplds,
                    selectedCrossSections, 1.0, 1.05, true,
                    out maxDisplacements, out compliances, out message,
                    out innsufficientCSOptiElemsSLS, out innsufficientCSOptiElemsULS, out unvonvergedCSOoptiElms,
                    out outModel1);


                

                // Second pass: slab optimization
                Karamba.Algorithms.OptiCroSec.solve(
                    outModel1, 1, true,
                    new List<string> { "LC0" }, new List<string> { "LC0" }, new List<string> { "LC0" },
                    10, 10, 5,
                    targetedDisp, targetedDispTol,
                    targetDeformDirs, targetDeformPlanes,
                    UtzTarget, targetVirtualWorkSlabs,
                    slabID, slabGrplds,
                    selectedShellCrossSections,
                    1.0,1.05,true,
                    out maxDisplacements, out compliances, out message,
                    out innsufficientCSOptiElemsSLS, out innsufficientCSOptiElemsULS, out unvonvergedCSOoptiElms,
                    out outModel2 );
            
            
                
            }

            Model optiModel = null;
            if (buldingType == 1)
            {
                optiModel = outModel3;
            }
            else if(buldingType == 2 || buldingType == 3)
            {
                optiModel = outModel2;
            }
           
                //ANALYZE THE MODEL AGAIN

                IReadOnlyList<double> maxDispOpti;
                IReadOnlyList<Vector3> outForceOpti;
                IReadOnlyList<double> outEnergyOpti;
                string warningOpti;
                modelNew = k3d.Algorithms.Analyze(optiModel, new List<string>() { "LC0" },
                                             out maxDispOpti, out outForceOpti, out outEnergyOpti, out warningOpti);

        

                var o_modelNew = new Karamba.GHopper.Models.GH_Model(modelNew);
              double displNew = maxDispOpti[0];

                DA.SetData(0, o_modelNew);
           // DA.SetDataList(3, Meshes3D);
        
          DA.SetData(1, displNew);
            //DA.SetDataList(2, ghPoints);

            //DA.SetDataList(5, AllSegments);
            //DA.SetDataList(6, pts);
            //DA.SetDataList(7, sideSurfaces);








            }





        private Point3d[] DivideSurfaceBySegments(Surface surface, int uCount, int vCount)
        {
            if (null == surface || uCount < 2 || vCount < 2)
                return new Point3d[0];

            var domainU = surface.Domain(0);
            var domainV = surface.Domain(1);

            var stepU = (domainU.Max - domainU.Min) / uCount;
            var stepV = (domainV.Max - domainV.Min) / vCount;

            var points = new List<Point3d>();

            for (var vi = 0; vi < vCount + 1; vi++)
            {
                for (var ui = 0; ui < uCount + 1; ui++)
                {
                    var u = domainU.Min + stepU * ui;
                    var v = domainV.Min + stepV * vi;
                    points.Add(surface.PointAt(u, v));
                }
            }

            return points.ToArray();
        }




        public FemMaterial CreateS355SteelMaterial()
        {
            double gamma = 78.5; // Specific weight (kN/m³)
            double youngModulus = 210000000; // E (kN/m² = MPa)
            double shearModulus = 81000000;     // G
            double tensileStrength = 35 * 10000;    // ft (MPa)
            double compressiveStrength = -35 * 10000; // fc (MPa)
            double alphaT = 1.2e-5;           // Thermal expansion

            FemMaterial s355Steel = new Karamba.Materials.FemMaterial_Isotrop(
                "Steel",                   // Family name
                "S355",                    // Material name
                youngModulus,
                shearModulus,
                shearModulus,
                gamma,
                tensileStrength,
                compressiveStrength,
                FemMaterial.FlowHypothesis.rankine,
                alphaT,
                null,                      // No color
                out _                      // Discard unused output
            );

            return s355Steel;
        }

        public FemMaterial_Orthotropic CreateT22ConiferousTimber()
        {

            return new FemMaterial_Orthotropic(
                     "Timber",
                    "T22 CLT",              //Name
                    1300e4,                 //Young modlus first dir 
                    43e4,                   // YOung modlus sec dir,
                    810e3,                 // in plane shear modlus 
                    0.0,                    // poison ratio
                    81e3,                   //Tranverse shear modlus !! Usikker på dette
                    81e3,
                    4.7,                    //Specific weight
                    22.0e3,                 // tensile strength in first dir    
                    0.4e3,                  // tensile strength in the second 
                    -26.0e3,                //Compresive strength in first dir     
                    -2.7e3,                 // compresive strength in sec di 
                    4.0e3, 
                    0.0, 
                    FemMaterial.FlowHypothesis.rankine,
                    5e-6,
                    5e-6,
                    null);
        }

        public FemMaterial_Orthotropic CreateT22_StrongOtherDirection()
        {
            return new FemMaterial_Orthotropic(
                "Timber",
                "T22 Strong-X",      // Name
                43e4,              // E1 - strong
                1300e4,                // E2 - weak
                810e3,                // G12 - weak in-plane shear
                0.0,                // ν12
                81e3,                // G13 - weak transverse
                81e3,                // G23 - weak transverse
                4.7,                 // ρ
                 22.0e3,              // ft1
                22.0e3,               // ft2
                -26.0e3,             // fc1
                -26.0e3,              // fc2
                4.0e3,               // t12
                0.0,
                FemMaterial.FlowHypothesis.rankine,
                5e-6,
                5e-6,
                null            );
        }



        public FemMaterial_Orthotropic CreateC30_37Concrete()
        {
            return new FemMaterial_Orthotropic(
                "Concrete",                 // Family
                "C30/37",                   // Name
                33000000,                   // E1 in kN/m² (33,000 MPa = 33e6 kN/m²)
                33000000,                   // E2 in kN/m²
                13750 * 1000,               // G12 in kN/m² (13750 MPa × 1000 = 13,750,000 kN/m²)
                0.2,                        // ν12 (Poisson's ratio)
                13750 * 1000,               // G31 in kN/m²
                13750 * 1000,               // G32 in kN/m²
                25.0,                       // γ (specific weight in kN/m³)
                2.0 * 1000,                 // ft1 in kN/m² (3.0 MPa × 1000 = 3000 kN/m²)
                2.0 * 1000,                 // ft2 in kN/m²
                -30.0 * 1000,               // fc1 in kN/m² (–30.0 MPa × 1000 = –30000 kN/m²)
                -30.0 * 1000,               // fc2 in kN/m²
                4.0,                 // t12 in kN/m² (4.0 MPa × 1000 = 4000 kN/m²)
                0.0,                        // f12 (Tsai-Wu coefficient)
                FemMaterial.FlowHypothesis.rankine, // Flow hypothesis
                1.0e-5,                     // αT1 (thermal expansion)
                1.0e-5,                     // αT2
                System.Drawing.Color.Gray   // Color
            );
        }


        public FemMaterial_Orthotropic Create32c_GulamTimber()
        {
            return new FemMaterial_Orthotropic(
                "GulamTimber",                // Family
                "32c",                     // Name
                1370e4,                  // E1 in kN/m² (34,000 MPa)
                42e3,                  // E2 in kN/m²
                85e4,              // G12 in kN/m² (14170 MPa × 1000)
                0.0,                       // ν12 (Poisson's ratio)
                85e4,              // G31 in kN/m²
                85e4,              // G32 in kN/m²
                5.0,                     // Specific weight (kN/m³)
                1.95e4,                // ft1 in kN/m² (3.5 MPa × 1000)
                0.05e4,               // ft2 in kN/m²
                -2.65e4,              // fc1 in kN/m² (–35.0 MPa × 1000)
                -0.3e4,              // fc2 in kN/m²
                2500,                // t12 in kN/m² (4.5 MPa × 1000)
                0.0,                       // f12 (Tsai-Wu coefficient)
                FemMaterial.FlowHypothesis.rankine,
                5.0e-6,                    // αT1 (thermal expansion)
                5.0e-6,                    // αT2
                System.Drawing.Color.Brown
            );
        }


        public FemMaterial_Orthotropic CreateC35_45Concrete()
        {
            return new FemMaterial_Orthotropic(
                "Concrete",                // Family
                "B35",                     // Name
                34000000,                  // E1 in kN/m² (34,000 MPa)
                34000000,                  // E2 in kN/m²
                14170 * 1000,              // G12 in kN/m² (14170 MPa × 1000)
                0.2,                       // ν12 (Poisson's ratio)
                14170 * 1000,              // G31 in kN/m²
                14170 * 1000,              // G32 in kN/m²
                25.0,                      // Specific weight (kN/m³)
                2330,                // ft1 in kN/m² (3.5 MPa × 1000)
                2330,                // ft2 in kN/m²
                -23330,              // fc1 in kN/m² (–35.0 MPa × 1000)
                -23300,              // fc2 in kN/m²
                0.0,                // t12 in kN/m² (4.5 MPa × 1000)
                0.0,                       // f12 (Tsai-Wu coefficient)
                FemMaterial.FlowHypothesis.rankine,
                1.0e-5,                    // αT1 (thermal expansion)
                1.0e-5,                    // αT2
                System.Drawing.Color.LightGray
            );
        }

        public FemMaterial_Orthotropic CreateC40_50Concrete()
        {
            return new FemMaterial_Orthotropic(
                "Concrete",                // Family
                "B40",                     // Name
                35000000,               // E1 in kN/m² (already provided as 35e6)
                35000000,                  // E2 in kN/m²
                14580 * 1000,              // G12 in kN/m² (14580 MPa × 1000)
                0.2,                       // Poisson’s ratio
                14580 * 1000,              // G31 in kN/m²
                14580 * 1000,              // G32 in kN/m²
                25.0,                      // Specific weight in kN/m³
                4.0 * 1000,                // ft1 in kN/m² (4.0 MPa × 1000)
                4.0 * 1000,                // ft2 in kN/m²
                -40.0 * 1000,              // fc1 in kN/m² (-40.0 MPa × 1000)
                -40.0 * 1000,              // fc2 in kN/m²
                5.0 * 1000,                // t12 in kN/m² (5.0 MPa × 1000)
                0.0,                       // f12 (Tsai-Wu coefficient)
                FemMaterial.FlowHypothesis.rankine,
                1.0e-5,                    // Thermal expansion αT1
                1.0e-5,                    // Thermal expansion αT2
                System.Drawing.Color.DarkGray
            );
        }
        public List<CroSec_Box> CreateSHSCrossSections(FemMaterial material)
        {
            // (b, t) in mm from your table
            var shsData = new List<(double b, double t)>
    {
        (90, 3.6), (90, 4.0), (90, 5.0), (90, 6.0), (90, 6.3), (90, 7.1), (90, 8.0), (90, 10.0), (90, 12.0),
        (100, 3.0), (100, 3.6), (100, 4.0), (100, 5.0), (100, 6.0), (100, 6.3), (100, 7.1), (100, 8.0), (100, 10.0), (100, 12.5), (100, 14.2),
        (110, 4.0), (110, 5.0), (110, 6.3), (130, 10.0), (130, 11.0), (130, 12.5), (130, 14.2), (130, 16.0),
        (140, 4.0), (140, 5.0), (140, 6.0), (140, 6.3), (140, 7.1), (140, 8.0), (140, 10.0), (140, 11.0), (140, 12.5), (140, 14.2), (140, 16.0),
        (150, 4.0), (150, 5.0), (150, 6.0), (150, 6.3), (150, 8.0), (150, 10.0), (150, 11.0), (150, 12.0), (150, 12.5), (150, 14.2), (150, 16.0),
        (160, 5.0), (160, 6.0), (160, 6.3), (160, 8.0), (160, 10.0), (160, 12.0), (160, 12.5), (160, 14.2), (160, 16.0)
    };

            var sections = new List<CroSec_Box>();

            for (int i = 0; i < shsData.Count; i++)
            {
                var (b, t) = shsData[i];
                string name = $"SHS_{(int)b}x{t:F1}";

                sections.Add(new CroSec_Box(
                    "SHS",
                    name,
                    "EN",
                    null,
                    material,
                    b / 10.0,   // height in cm
                    b / 10.0,   // upper flange width in cm
                    b / 10.0,   // lower flange width in cm
                    t / 10.0,   // upper flange thickness in cm
                    t / 10.0,   // lower flange thickness in cm
                    t / 10.0,   // web thickness in cm
                    0.5,
                    0.0
                ));
            }

            return sections;
        }

        public List<CroSec_Box> GenerateBoxCrossSections(FemMaterial s355Steel)
        {
            // Define the list of candidate dimensions for the beam table.
            var filteredBeamTable = new List<(double Height, double WidthUF, double WidthLF, double ThickUF, double ThickLF, double ThickWeb)>
    {
        (17.0, 25.0, 51.0, 1.5, 1.0, 1.0),
        (17.0, 25.0, 51.0, 2.0, 1.0, 1.0),
        (17.0, 25.0, 51.0, 2.5, 1.0, 1.0),
        (17.0, 25.0, 51.0, 1.5, 1.2, 1.0),
        (17.0, 25.0, 51.0, 2.0, 1.2, 1.0),
        (17.0, 25.0, 51.0, 2.5, 1.2, 1.0),
        (17.0, 25.0, 51.0, 1.5, 1.5, 1.0),
        (17.0, 25.0, 51.0, 2.0, 1.5, 1.0),
        (17.0, 25.0, 51.0, 2.5, 1.5, 1.0),
        (24.0, 25.0, 51.0, 1.5, 1.2, 1.0),
        (24.0, 25.0, 51.0, 2.0, 1.2, 1.0),
        (24.0, 25.0, 51.0, 2.5, 1.2, 1.0),
        (24.0, 25.0, 51.0, 1.5, 1.5, 1.0),
        (24.0, 25.0, 51.0, 2.0, 1.5, 1.0),
        (24.0, 25.0, 51.0, 2.5, 1.5, 1.0),
        (24.0, 25.0, 51.0, 2.0, 2.0, 1.0),
        (24.0, 25.0, 51.0, 2.5, 2.0, 1.0),
        (24.0, 25.0, 51.0, 3.0, 2.0, 1.0),
        (29.0, 25.0, 51.0, 1.5, 1.5, 1.0),
        (29.0, 25.0, 51.0, 2.0, 1.5, 1.0),
        (29.0, 25.0, 51.0, 2.5, 1.5, 1.0),
        (29.0, 25.0, 51.0, 2.0, 2.0, 1.0),
        (29.0, 25.0, 51.0, 3.0, 2.0, 1.0),
        (29.0, 25.0, 51.0, 3.5, 2.0, 1.0),
        (29.0, 25.0, 51.0, 2.5, 2.5, 1.0),
        (29.0, 25.0, 51.0, 3.0, 2.5, 1.0),
        (29.0, 25.0, 51.0, 3.5, 2.5, 1.0),
        (29.0, 25.0, 51.0, 4.0, 2.5, 1.0),
        (29.0, 25.0, 51.0, 4.0, 3.0, 1.0),
        (29.0, 25.0, 51.0, 4.5, 3.0, 1.0),
        (29.0, 25.0, 51.0, 4.5, 3.5, 1.0),
        (29.0, 25.0, 51.0, 5.0, 3.5, 1.0),
        (29.0, 25.0, 51.0, 5.0, 4.0, 1.0),
        (29.0, 25.0, 51.0, 5.5, 4.0, 1.0),
        (29.0, 25.0, 51.0, 5.5, 4.5, 1.0),
        (29.0, 25.0, 51.0, 6.0, 4.5, 1.0),
        (29.0, 25.0, 51.0, 6.0, 5.0, 1.0),
        (29.0, 25.0, 51.0, 6.5, 5.0, 1.0),
        (29.0, 25.0, 51.0, 6.5, 5.5, 1.0),
        (29.0, 25.0, 51.0, 7.0, 5.5, 1.0),
        (29.0, 25.0, 51.0, 7.0, 6.0, 1.0),
        (29.0, 25.0, 51.0, 7.5, 6.0, 1.0),
        (29.0, 25.0, 51.0, 7.5, 6.5, 1.0),
    };

            // List to hold the created box cross sections.
            var boxCrossSections = new List<CroSec_Box>();

            // Create a new cross section for each candidate dimension.
            for (int i = 0; i < filteredBeamTable.Count; i++)
            {
                // Using modulus here allows for cycling through the candidate list if needed.
                var candidate = filteredBeamTable[i % filteredBeamTable.Count];

                var boxSection = new CroSec_Box(
                    "HSQ",                           // Family
                    "HSQ_Section_" + i,              // Unique name for the cross section
                    "Generic",                       // Country
                    null,                            // Color (null indicates no color is set)
                    s355Steel,                       // Material
                    candidate.Height,                // Height
                    candidate.WidthUF,               // Upper flange width
                    candidate.WidthLF,               // Lower flange width
                    candidate.ThickUF,               // Upper flange thickness
                    candidate.ThickLF,               // Lower flange thickness
                    candidate.ThickWeb,              // Web thickness
                    0.5,                             // Additional parameter (adjust as needed)
                    0                                // Fillet radius (0 for sharp corners)
                );

                boxCrossSections.Add(boxSection);
            }

            return boxCrossSections;
        }


        public List<CroSec_Box> GenerateBoxCrossSectionsFSQ(FemMaterial s355Steel)
        {
            // Define the list of candidate dimensions for the beam table.
            var filteredBeamTable = new List<(double Height, double WidthUF, double WidthLF, double ThickUF, double ThickLF, double ThickWeb)>
    {

        (17.0, 15.0, 31.0, 1.5, 1.2, 0.6),
        (17.0, 15.0, 31.0, 1.5, 1.5, 0.6),
        (17.0, 15.0, 31.0, 2.0, 1.2, 0.6),
        (17.0, 15.0, 31.0, 2.5, 1.2, 0.6),
        (17.0, 15.0, 31.0, 1.5, 1.5, 0.6),
        (17.0, 15.0, 31.0, 2.0, 1.5, 0.6),
        (17.0, 15.0, 31.0, 2.5, 1.5, 0.6),
        (24.0, 15.0, 31.0, 1.5, 1.2, 0.6),
        (24.0, 15.0, 31.0, 2.0, 1.2, 0.6),
        (24.0, 15.0, 31.0, 2.5, 1.2, 0.6),
        (24.0, 15.0, 31.0, 1.5, 1.5, 0.6),
        (24.0, 15.0, 31.0, 2.0, 1.5, 0.6),
        (24.0, 15.0, 31.0, 2.5, 1.5, 0.6),
        (29.0, 15.0, 31.0, 2.5, 1.2, 0.6),
        (29.0, 15.0, 31.0, 1.5, 1.5, 0.6),
        (29.0, 15.0, 31.0, 2.0, 1.5, 0.6),
        (29.0, 15.0, 31.0, 2.5, 1.5, 0.6),
        (29.0, 15.0, 31.0, 2.0, 2.0, 0.6),
        (37.0, 15.0, 31.0, 2.0, 1.2, 0.6),
        (37.0, 15.0, 31.0, 2.5, 1.2, 0.6),
        (37.0, 15.0, 31.0, 1.5, 1.5, 0.6),
        (37.0, 15.0, 31.0, 2.5, 1.5, 0.6),
        (37.0, 15.0, 31.0, 2.0, 2.0, 0.6)
    };

            // List to hold the created box cross sections.
            var boxCrossSections = new List<CroSec_Box>();

            // Create a new cross section for each candidate dimension.
            for (int i = 0; i < filteredBeamTable.Count; i++)
            {
                // Using modulus here allows for cycling through the candidate list if needed.
                var candidate = filteredBeamTable[i % filteredBeamTable.Count];

                var boxSection = new CroSec_Box(
                    "FSQ",                           // Family
                    "FSQ_Section_" + i,              // Unique name for the cross section
                    "Generic",                       // Country
                    null,                            // Color (null indicates no color is set)
                    s355Steel,                       // Material
                    candidate.Height,                // Height
                    candidate.WidthUF,               // Upper flange width
                    candidate.WidthLF,               // Lower flange width
                    candidate.ThickUF,               // Upper flange thickness
                    candidate.ThickLF,               // Lower flange thickness
                    candidate.ThickWeb,              // Web thickness
                    0.5,                             // Additional parameter (adjust as needed)
                    0                                // Fillet radius (0 for sharp corners)
                );

                boxCrossSections.Add(boxSection);
            }

            return boxCrossSections;
        }


        public List<CroSec_I> GenerateHEAProfiles(FemMaterial s355Steel)
        {
            // Table columns:
            // (ProfileName, Weight_kgPerM, b_mm, h_mm, s_mm, t_mm, r_mm, Ix_cm4, Wely_x_cm3)
            var heaData = new List<(string name, double weight, double b_mm, double h_mm, double s_mm, double t_mm, double r_mm, double Ix_cm4, double Wely_x_cm3)>
    {
        ("HEA 100", 16.7,   100,  96, 5.0,  8.0, 12,    349.2,   72.6),
        ("HEA 120", 19.9,   120, 114, 5.0,  8.0, 12,    606.2,   106.3),
        ("HEA 140", 24.7,   140, 133, 5.5,  8.5, 13,   1033.0,   155.4),
        ("HEA 160", 30.4,   160, 152, 6.0,  9.0, 15,   1673.0,   220.1),
        ("HEA 180", 35.5,   180, 171, 6.0,  9.5, 15,   2510.0,   293.6),
        ("HEA 200", 42.3,   200, 190, 6.5, 10.0, 18,   3692.0,   388.6),
        ("HEA 220", 47.8,   220, 210, 7.0, 11.0, 18,   5410.0,   515.2),
        ("HEA 240", 55.2,   240, 230, 7.5, 12.0, 21,   7763.0,   675.1),
        ("HEA 260", 61.3,   260, 250, 7.5, 12.5, 21,  10450.0,  836.1),
        ("HEA 280", 68.0,   280, 270, 8.0, 13.0, 24,  13900.0,  1013.0),
        ("HEA 300", 74.0,   300, 290, 8.5, 14.0, 24,  18260.0,  1260.0),
        ("HEA 320", 82.0,   300, 310, 9.0, 15.5, 27,  22930.0,  1479.0),
        ("HEA 340", 90.0,   300, 330, 9.5, 16.0, 27,  28370.0,  1678.0),
        ("HEA 360",100.0,   300, 350,10.0, 17.0, 27,  33090.0,  1891.0),
        ("HEA 400",120.0,   300, 390,11.0,19.0, 30,  45090.0,  2311.0),
        ("HEA 450",129.0,   300, 440,11.5,21.0, 33,  63720.0,  2896.0),
        ("HEA 500",139.0,   300, 490,12.0,23.0, 36,  86970.0,  3550.0),
        ("HEA 550",153.0,   300, 540,12.5,24.0, 39, 111900.0,  4146.0),
        ("HEA 600",166.0,   300, 590,13.0,25.0, 42, 141200.0,  4787.0),
        ("HEA 650",178.0,   300, 640,13.5,26.0, 45, 175200.0,  5474.0),
        ("HEA 700",191.0,   300, 690,14.5,27.0, 48, 215300.0,  6241.0),
        ("HEA 800",210.0,   300, 790,15.0,28.0, 54, 308400.0,  7662.0),
        ("HEA 900",239.0,   300, 890,16.0,30.0, 60, 422100.0, 9485.0),
        ("HEA1000",272.0,   300, 990,16.5,31.0, 66, 553800.0, 11190.0) // placeholder
    };

            var heaProfiles = new List<CroSec_I>();

            foreach (var row in heaData)
            {
                // convert from mm → cm (your model units)
                double h_cm = row.h_mm / 10.0;   // overall depth
                double bf_cm = row.b_mm / 10.0;   // flange width
                double tf_cm = row.t_mm / 10.0;   // flange thickness
                double tw_cm = row.s_mm / 10.0;   // web thickness
                double r_cm = row.r_mm / 10.0;   // fillet radius

                var sec = new CroSec_I(
                  "HEA",            // family
                  row.name,         // e.g. "HEA 100"
                  "Generic",        // country or standard
                  null,             // color (optional)
                  s355Steel,        // material
                  h_cm,             // 1) overall height
                  bf_cm,            // 2) lower flange width
                  bf_cm,            // 3) upper flange width  (same for symmetric)
                  tf_cm,            // 4) lower flange again   (symmetric)
                  tf_cm,            // 5) upper-flange thickness — we’ll overload this slot
                  tw_cm            // 6) web thickness        — note the order must match Karamba’s signature
                           
                );

                heaProfiles.Add(sec);
            }

            return heaProfiles;
        }

        public List<CroSec_Trapezoid> CreateProfilesInSituConcrete(FemMaterial material, double minSizeCm = 10.0, double maxSizeCm = 40.0)
        {
            var profiles = new List<CroSec_Trapezoid>();

            for (double width = minSizeCm; width <= maxSizeCm; width += 1.0)
            {
                for (double height = minSizeCm; height <= maxSizeCm; height += 1.0)
                {
                    string name = $"RectTrap_{width}x{height}";

                    var rectTrapezoid = new CroSec_Trapezoid(
                        "Concrete Beams",           // Family name
                        name,                 // Unique name
                        "Poland",          // Country (optional)
                        null,                 // Color (optional)
                        material,             // FemMaterial (e.g. GL32c timber)
                        height,               // Height (cm)
                        width,                // Lower flange width (cm)
                        width                 // Upper flange width (same for rectangle)
                    );

                    profiles.Add(rectTrapezoid);
                }
            }

            return profiles;
        }

        public static List<Support> CreateSlabSupports(
    List<Brep> slabs, KarambaCommon.Toolkit k3d, double zTol = 1e-6)
        {
            var cond = new List<bool> { true, true, true, false, false, false };
            var plane = new Plane3(
                new Point3(0, 0, 0),
                new Vector3(1, 0, 0),
                new Vector3(0, 1, 0));
            var pts = new List<Point3d>();

            foreach (var brep in slabs)
                foreach (var v in brep.Vertices)
                    // pick only those vertices at the slab’s own Z
                    if (Math.Abs(v.Location.Z - brep.Vertices[0].Location.Z) < zTol)
                        pts.Add(v.Location);

            return pts
              .Select(p => k3d.Support.Support(
                  new Karamba.Geometry.Point3(p.X, p.Y, p.Z),
                  cond, plane))
              .ToList();
        }

        public List<CroSec_Circle> CreateSolidCircularProfiles(FemMaterial material)
        {
            var profiles = new List<CroSec_Circle>();

            for (double diameterMm = 10.0; diameterMm <= 200.0; diameterMm += 5.0)
            {
                string name = $"Circle_{diameterMm}mm";

                var circleProfile = new CroSec_Circle(
                    "Bracing Steel",             // Family name
                    name,                        // Unique name
                    "EU",                        // Country (optional)
                    null,                        // Color (optional)
                    material,                    // Material (e.g., S355)
                    diameterMm / 10.0,           // Diameter in cm
                    diameterMm / 10.0            // Thickness = diameter → solid
                );

                profiles.Add(circleProfile);
            }

            return profiles;
        }


        public List<CroSec_Trapezoid> CreateGlulamCrossSections(FemMaterial material)
        {
            var dimensions = new List<(int width_mm, int height_mm)>
    {
// 90 mm width
(90, 135), (90, 180), (90, 225), (90, 270), (90, 315), (90, 360), (90, 405), (90, 450),
// 115 mm width
(115, 180), (115, 225), (115, 270), (115, 315), (115, 360), (115, 405),
// 140 mm width
(140, 180), (140, 225), (140, 270), (140, 315), (140, 360), (140, 405), (140, 450), (140, 495),
// 165 mm width
(165, 225), (165, 270), (165, 315), (165, 360), (165, 405), (165, 450), (165, 495), (165, 540),
// 190 mm width
(190, 270), (190, 315), (190, 360), (190, 405), (190, 450), (190, 495), (190, 540), (190, 585),
// 215 mm width
(215, 315), (215, 360), (215, 405), (215, 450), (215, 495), (215, 540), (215, 585), (215, 630),
// 240 mm width
(240, 360), (240, 405), (240, 450), (240, 495), (240, 540), (240, 585), (240, 630), (240, 675),
// 265 mm width
(265, 405), (265, 450), (265, 495), (265, 540), (265, 585), (265, 630), (265, 675), (265, 720)
    };

            var profiles = new List<CroSec_Trapezoid>();

            foreach (var (width_mm, height_mm) in dimensions)
            {
                double width_cm = width_mm / 10.0;
                double height_cm = height_mm / 10.0;
                string name = $"GL_{width_mm}x{height_mm}";

                var crossSection = new CroSec_Trapezoid(
                    family: "Glulam",
                    name: name,
                    country: "NO",       // Or your country code
                    color: null,
                    material: material,
                    height: height_cm,
                    lf_width: width_cm,
                    uf_width: width_cm  // same as lower to make it rectangular
                );

                profiles.Add(crossSection);
            }

            return profiles;
        }



        public static (double Height, double SelfWeight) GetHollowCoreProfiles(double spanLength)
        {
            var slabTable = new List<(double MaxSpan, double Height, double SelfWeight)>
    {
        (10, 20.0, 2.6),
        (13, 26.5, 3.7),
        (15, 32.0, 4.2),
        (17, 40.0, 5.0),
        (19, 50.0, 6.8)
    };

            foreach (var row in slabTable)
            {
                if (spanLength <= row.MaxSpan)
                    return (row.Height, row.SelfWeight);
            }

            // If no match found, return the last row
            var fallback = slabTable.Last();
            return (fallback.Height, fallback.SelfWeight);
        }

      

        public List<CroSec_Shell> CreateConcreteShellCrossSections(FemMaterial concreteMaterial, double minHeightCm = 10.0, double maxHeightCm = 30.0, double incrementCm = 2.5)
        {
            var shellSections = new List<CroSec_Shell>();

            for (double height = minHeightCm; height <= maxHeightCm; height += incrementCm)
            {
                double heightMeters = height / 100.0; // convert cm to meters (Karamba expects meters for shell thickness)
                string name = $"ConcreteShell_{height}cm";

                var materials = new List<FemMaterial> { concreteMaterial };
                var eccZ = new List<double> { 0.0 };                 // No eccentricity for single layer
                var elemHeights = new List<double> { heightMeters }; // Uniform thickness

                var shell = new CroSec_Shell(
                    "ConcreteSlab",
                    name,
                    "Generated",
                    null,
                    materials,
                    eccZ,
                    elemHeights
                );

                shellSections.Add(shell);
            }

            return shellSections;
        }


        public List<CroSec_Shell> CreateTimberShells(FemMaterial material)
        {
            var thicknesses_mm = new List<int> { 95, 120, 145, 170, 195, 220, 250, 280, 300, 320, 340, 360 };
            var shellSections = new List<CroSec_Shell>();

            foreach (int thickness_mm in thicknesses_mm)
            {
                double thickness_m = thickness_mm / 1000.0; // Convert mm to meters (Karamba expects meters)

                string name = $"Shell_{thickness_mm}mm";

                var materials = new List<FemMaterial> { material };  // Single-layer
                var ecc_z = new List<double> { 0.0 };                // No eccentricity
                var heights = new List<double> { thickness_m };      // Single thickness layer

                var shell = new CroSec_Shell(
                    "TimberShell",
                    name,
                    "Generated",
                    null,
                    materials,
                    ecc_z,
                    heights
                );

                shellSections.Add(shell);
            }

            return shellSections;
        }

        public static void CreateKarambaBeams(
        List<Line> axisLines,
         string prefix,
        out List<Karamba.Geometry.Line3> karambaLines,
        out List<string> beamIDs)
        {
            karambaLines = new List<Karamba.Geometry.Line3>();
            beamIDs = new List<string>(); // <- Add this line

            int idx = 0;
            foreach (var rhinoLine in axisLines)
            {
                var kLine = new Karamba.Geometry.Line3(
                    new Karamba.Geometry.Point3(rhinoLine.FromX, rhinoLine.FromY, rhinoLine.FromZ),
                    new Karamba.Geometry.Point3(rhinoLine.ToX, rhinoLine.ToY, rhinoLine.ToZ)
                );
                karambaLines.Add(kLine);
            }
        }


        public class Point3dComparer : IEqualityComparer<Point3d>
        {
            private double tolerance;
            public Point3dComparer(double tol)
            {
                tolerance = tol;
            }
            public bool Equals(Point3d p1, Point3d p2)
            {
                return p1.DistanceTo(p2) < tolerance;
            }
            public int GetHashCode(Point3d p)
            {
                unchecked
                {
                    int hash = 17;
                    hash = hash * 23 + p.X.GetHashCode();
                    hash = hash * 23 + p.Y.GetHashCode();
                    hash = hash * 23 + p.Z.GetHashCode();
                    return hash;
                }
            }
        }


        public static List<Support> CreateSupports(
        List<Line> columns,
        KarambaCommon.Toolkit k3d,
       double zTolerance = 1e-6)
        {
            var cond = new List<bool> { true, true, true, false, false, false }; // All DOFs locked
            var supportPlane = new Karamba.Geometry.Plane3(
                new Karamba.Geometry.Point3(0, 0, 0),
                new Karamba.Geometry.Vector3(1, 0, 0),
                new Karamba.Geometry.Vector3(0, 1, 0)
            );

            List<Point3d> supportPoints = new List<Point3d>();

            foreach (Line col in columns)
            {
                if (Math.Abs(col.FromZ) < zTolerance) supportPoints.Add(col.From);
                if (Math.Abs(col.ToZ) < zTolerance) supportPoints.Add(col.To);
            }

            // Convert to Karamba Points and create supports
            return supportPoints
                .Select(pt => k3d.Support.Support(pt.Convert(), cond, supportPlane))
                .ToList();
        }

        public static List<Support> CreateTopSupports(
    List<Line> columns,
    KarambaCommon.Toolkit k3d,
    double zTolerance = 1e-6)
        {
            // same restraint conditions as your base method
            var cond = new List<bool> { true, true, true, false, false, false };
            var supportPlane = new Karamba.Geometry.Plane3(
                new Karamba.Geometry.Point3(0, 0, 0),
                new Karamba.Geometry.Vector3(1, 0, 0),
                new Karamba.Geometry.Vector3(0, 1, 0)
            );

            // Gather all endpoint Z‐values to find global max
            var allZ = columns
                .SelectMany(l => new[] { l.FromZ, l.ToZ });
            if (!allZ.Any()) return new List<Support>();

            double maxZ = allZ.Max();

            // Collect every endpoint that lies within tolerance of that maxZ
            var topPoints = new List<Point3d>();
            foreach (var col in columns)
            {
                if (Math.Abs(col.FromZ - maxZ) < zTolerance)
                    topPoints.Add(col.From);
                if (Math.Abs(col.ToZ - maxZ) < zTolerance)
                    topPoints.Add(col.To);
            }

            // Convert to Karamba supports
            return topPoints
                .Select(pt => k3d.Support.Support(
                    new Karamba.Geometry.Point3(pt.X, pt.Y, pt.Z),
                    cond,
                    supportPlane))
                .ToList();
        }
        public static List<Support> CreateShearWallSupports(
    List<GH_Point> wallPoints,
    KarambaCommon.Toolkit k3d,
    double zTolerance = 1e-6)
        {
            // Lock X, Y, Z translations; free rotations
            var cond = new List<bool> { true, true, true, true, true, true };

            // Define a horizontal support plane
            var supportPlane = new Karamba.Geometry.Plane3(
                new Karamba.Geometry.Point3(0, 0, 0),
                new Karamba.Geometry.Vector3(1, 0, 0),
                new Karamba.Geometry.Vector3(0, 1, 0)
            );

            // Filter wall points at Z = 0 and convert to Karamba supports
            return wallPoints
                .Where(ghpt => Math.Abs(ghpt.Value.Z) < zTolerance)
                .Select(ghpt => k3d.Support.Support(
                    new Karamba.Geometry.Point3(ghpt.Value.X, ghpt.Value.Y, ghpt.Value.Z),
                    cond,
                    supportPlane))
                .ToList();
        }

        public static void DivideLinesByCount(
    List<Line> inputLines,
    int segmentCount,
    bool includeEnds,
    out List<Line> dividedSegments,
    out List<Point3d> divisionPoints)
        {
            dividedSegments = new List<Line>();
            divisionPoints = new List<Point3d>();

            foreach (Line line in inputLines)
            {
                Curve curve = new LineCurve(line);
                Point3d[] points;
                double[] tValues = curve.DivideByCount(segmentCount, includeEnds, out points);

                if (points == null || points.Length < 2)
                    continue;

                // Create segments between consecutive points
                for (int i = 0; i < points.Length - 1; i++)
                {
                    dividedSegments.Add(new Line(points[i], points[i + 1]));
                }

                divisionPoints.AddRange(points);
            }
        }







        public FemMaterial_Orthotropic CreateT22StrongBothDir()
        {
            return new FemMaterial_Orthotropic("timber", "T22", 1300e4, 1300e4, 650e3, 0.0, 81e3, 81e3, 4.7, 3.1e4, 3.1e4, -2.6e4, -2.6e4, 0.0, 0.0, FemMaterial.FlowHypothesis.rankine,
                5.0e-6, 5.0e-6, null);
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
            get { return new Guid("812402F0-02B5-4702-8078-44CB95E02590"); }
        }
    }
}