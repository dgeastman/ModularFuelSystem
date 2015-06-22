﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using UnityEngine;
using KSP;
using SolverEngines;

namespace RealFuels
{
    public class ModuleEnginesRF : ModuleEnginesSolver
    {
        #region Fields
        [KSPField]
        public double chamberNominalTemp = 0d;
        [KSPField]
        public double extHeatkW = 0d;

        [KSPField]
        public float flowMultMin = 0.01f;

        [KSPField]
        public float flowMultCap = float.MaxValue;
        [KSPField]
        public float flowMultCapSharpness = 2f;
        [KSPField]
        public bool multFlow = true;

        [KSPField]
        public bool usesAir = false;


        [KSPField]
        public double varyThrust = 1d;

        

        #region Thrust Curve
        [KSPField]
        public bool useThrustCurve = false;
        [KSPField]
        public bool thrustCurveUseTime = false;
        [KSPField]
        public FloatCurve thrustCurve;
        [KSPField]
        public string curveResource = "";

        protected int curveProp = -1;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "% Rated Thrust", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveDisplay = 100f;
        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Fuel Ratio", guiUnits = "%", guiFormat = "F3")]
        public float thrustCurveRatio = 1f;

        [KSPField(isPersistant = false, guiActive = false, guiActiveEditor = false, guiName = "Ignited for ", guiUnits = "s", guiFormat = "F3")]
        public float curveTime = 0f;
        #endregion


        protected bool instantThrottle = false;
        protected float throttleResponseRate;
        protected SolverRF rfSolver = null;

        #region Ullage
        [KSPField]
        public Vector3 thrustAxis;

        [KSPField]
        public bool pressureFed = false;

        [KSPField]
        public bool ullage = false;

        [KSPField(guiName = "Ignitions Remaining")]
        public int ignitions = -1;

        [KSPField(guiName = "Propellant Status")]
        string propellantStatus = "Stable";

        public Ullage.UllageSet ullageSet;
        protected bool oldIgnitionState = false;
        protected bool propellantsOK = true;
        #endregion
        #endregion

        #region Overrides
        public override void CreateEngine()
        {
            rfSolver = new SolverRF();
            if(!useAtmCurve)
                atmCurve = null;
            if(!useVelCurve)
                velCurve = null;
            if (!useThrustCurve)
                thrustCurve = null;
            
            // FIXME quick temp hax
            if (useAtmCurve)
            {
                if (maxEngineTemp == 0d)
                    maxEngineTemp = 2000d;
                if (chamberNominalTemp == 0d)
                    chamberNominalTemp = 950d;
            }
            else
            {
                if (maxEngineTemp == 0d)
                    maxEngineTemp = 3600d;
                if (chamberNominalTemp == 0d)
                    chamberNominalTemp = 3400d;
                if (tempGaugeMin == 0.8d)
                    tempGaugeMin = 0.95d;
            }
            double thrustVariation = varyThrust * RFSettings.Instance.varyThrust;
            chamberNominalTemp *= (1d - varyThrust);

            rfSolver.InitializeOverallEngineData(
                minFuelFlow,
                maxFuelFlow,
                atmosphereCurve,
                atmCurve,
                velCurve,
                throttleResponseRate,
                chamberNominalTemp,
                machLimit,
                machHeatMult,
                flowMultMin,
                flowMultCap,
                flowMultCapSharpness,
                multFlow,
                thrustVariation,
                (float)part.name.GetHashCode());

            engineSolver = rfSolver;
        }
        public override void OnAwake()
        {
            base.OnAwake();
            if (thrustCurve == null)
                thrustCurve = new FloatCurve();
        }
        public override void OnLoad(ConfigNode node)
        {
            if (thrustCurve == null)
                thrustCurve = new FloatCurve();

            base.OnLoad(node);
            int pCount = propellants.Count;
            // thrust curve
            useThrustCurve = false;
            if (node.HasNode("thrustCurve") && node.HasValue("curveResource"))
            {
                if (node.GetValue("curveResource") != curveResource)
                {
                    Debug.Log("*RFE* ERROR: curveResource doesn't match node's!");
                    curveResource = node.GetValue("curveResource");
                }
                if (thrustCurve == null)
                {
                    Debug.Log("*RFE* ERROR: have curve node but thrustCurve is null!");
                    thrustCurve = new FloatCurve();
                    thrustCurve.Load(node.GetNode("thrustCurve"));
                }

                if (curveResource != "")
                {
                    for (int i = 0; i < pCount; ++i)
                    {
                        if (propellants[i].name.Equals(curveResource))
                        {
                            curveProp = i;
                            break;
                        }
                    }
                    if (curveProp != -1)
                    {
                        useThrustCurve = true;
                    }
                }
            }

            // Set from propellants
            bool instantThrottle = true;
            for (int i = 0; i < pCount; ++i)
            {
                if (RFSettings.Instance.instantThrottleProps.Contains(propellants[i].name))
                {
                    instantThrottle = false;
                }
                // any other stuff
            }

            // FIXME calculating throttle change rate
            if (!instantThrottle)
                throttleResponseRate = (float)(10d / Math.Sqrt(Math.Sqrt(part.mass * maxThrust)));
            else
                throttleResponseRate = 1000000f;

            // set fields
            Fields["thrustCurveDisplay"].guiActive = useThrustCurve;
            CreateEngine();

            // Get thrust axis (only on create prefabs)
            if (part.partInfo == null || part.partInfo.partPrefab == null)
            {
                Debug.Log("Created thrust axis for " + part.name);
                thrustAxis = Vector3.zero;
                int tCount = 0;
                foreach(Transform t in part.FindModelTransforms(thrustVectorTransformName))
                {
                    thrustAxis += t.forward;
                    ++tCount;
                }
                thrustAxis /= (float)tCount;
            }

            // ullage
            if (node.HasNode("Ullage"))
            {
                if (ullageSet == null)
                    ullageSet = new Ullage.UllageSet(this);
                ullageSet.Load(node.GetNode("Ullage"));
            }
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            if (ullageSet != null)
            {
                ConfigNode ullageNode = new ConfigNode("Ullage");
                ullageSet.Save(ullageNode);
                node.AddNode(ullageNode);
            }
        }
        public override void Start()
        {
            base.Start();
            if (ullageSet == null)
                ullageSet = new Ullage.UllageSet(this);

            Fields["ignitions"].guiActive = Fields["ignitions"].guiActiveEditor = (ignitions >= 0);
            Fields["propellantStatus"].guiActive = Fields["propellantStatus"].guiActiveEditor = (pressureFed || ullage);
            

        }
        public override void OnStart(PartModule.StartState state)
        {
            base.OnStart(state);
            
            Fields["thrustCurveDisplay"].guiActive = useThrustCurve && state != StartState.Editor;
        }
        public override void UpdateThrottle()
        {
            if (throttleLocked)
                requestedThrottle = 1f;

            if (instantThrottle)
                currentThrottle = requestedThrottle * thrustPercentage * 0.01f;
            else
            {
                
                float requiredThrottle = requestedThrottle * thrustPercentage * 0.01f;
                float deltaT = TimeWarp.fixedDeltaTime;

                float d = requiredThrottle - currentThrottle;
                float thisTick = throttleResponseRate * deltaT;
                if (Math.Abs((double)d) > thisTick)
                {
                    if (d > 0f)
                        currentThrottle += thisTick;
                    else
                        currentThrottle -= thisTick;
                }
                else
                    currentThrottle = requiredThrottle;
            }
            actualThrottle = Mathf.RoundToInt(currentThrottle * 100f);
        }
        
        // from SolverEngines but we don't play FX here.
        [KSPEvent(guiActive = true, guiName = "Activate Engine")]
        public override void Activate()
        {
            if (!allowRestart && engineShutdown)
            {
                return; // If the engines were shutdown previously and restarting is not allowed, prevent restart of engines
            }
            if (noShieldedStart && part.ShieldedFromAirstream)
            {
                ScreenMessages.PostScreenMessage("<color=orange>[" + part.partInfo.title + "]: Cannot activate while stowed!</color>", 6f, ScreenMessageStyle.UPPER_LEFT);
                return;
            }

            EngineIgnited = true;
            if (allowShutdown) Events["Shutdown"].active = true;
            else Events["Shutdown"].active = false;
            Events["Activate"].active = false;
        }
        public override void UpdateFlightCondition(EngineThermodynamics ambientTherm, double altitude, Vector3d vel, double mach, bool oxygen)
        {
            bool throttledUp = false;
            if (currentThrottle > 0f)
                throttledUp = true;
            else
                oldIgnitionState = false;

            // handle ignition
            if (EngineIgnited && throttledUp)
            {
                if (!oldIgnitionState && propellantsOK)
                {
                    if (ignitions == 0)
                    {
                        EngineIgnited = false; // don't play shutdown FX, just fail.
                    }
                    else
                    {
                        if (ignitions > 0)
                            ignitions--;

                        PlayEngageFX();    
                    }   
                }
            }
            else
                propellantsOK = true; // reset

            // Ullage
            bool ullageOK = true;
            bool pressureOK = ullageSet.PressureOK();

            if (ullage)
            {
                propellantStatus = ullageSet.GetUllageState();
                if (EngineIgnited && propellantsOK && throttledUp)
                {
                    double state = ullageSet.GetUllageStability();
                    double testValue = Math.Pow(state, RFSettings.Instance.stabilityPower);
                    if (UnityEngine.Random.value > testValue)
                    {
                        FlightLogger.eventLog.Add("[" + FormatTime(vessel.missionTime) + "] " + part.partInfo.title + " had vapor in its feed line and shut down.");
                        ullageOK = false;
                    }
                }
            }
            if(pressureFed)
            {
                if (!pressureOK)
                {
                    propellantStatus = "Feed pressure too low"; // override ullage status indicator
                }
                else if (!HighLogic.LoadedSceneIsFlight) // ullage always ok in editor, so safe to override
                    propellantStatus = "Feed pressure OK";
            }
            rfSolver.SetPropellantStatus(pressureOK, ullageOK);
            oldIgnitionState = throttledUp && EngineIgnited && pressureOK && ullageOK; // if one fails, we're not ignited anymore
            propellantsOK &= pressureOK && ullageOK; // set false once we're not ignited, but don't set true.

            // do thrust curve
            if (oldIgnitionState && useThrustCurve && HighLogic.LoadedSceneIsFlight)
            {
                thrustCurveRatio = (float)((propellants[curveProp].totalResourceAvailable / propellants[curveProp].totalResourceCapacity));
                if (thrustCurveUseTime)
                {
                    thrustCurveDisplay = thrustCurve.Evaluate(curveTime);
                    if (EngineIgnited)
                    {
                        curveTime += TimeWarp.fixedDeltaTime;
                    }
                }
                else
                {
                    thrustCurveDisplay = thrustCurve.Evaluate(thrustCurveRatio);
                }
                rfSolver.UpdateThrustRatio(thrustCurveDisplay);
                thrustCurveDisplay *= 100f;
            }

            // Set part temp
            rfSolver.SetPartTemp(part.temperature);

            // do heat
            heatProduction = (float)(extHeatkW / PhysicsGlobals.InternalHeatProductionFactor * part.thermalMassReciprocal);

            // Manually run base method code
            // In flight, these are the same and this will just return
            this.ambientTherm.CopyFrom(ambientTherm);

            engineSolver.SetEngineState(EngineIgnited, lastPropellantFraction);
            engineSolver.SetFreestreamAndInlet(ambientTherm, inletTherm, altitude, mach, vel, oxygen);
            engineSolver.CalculatePerformance(areaRatio, currentThrottle, flowMult, ispMult);
        }
        #endregion

        #region Info
        protected string ThrottleString()
        {
            string output = "";
            double throttleP = 0d;
            if(minFuelFlow > 0d)
                throttleP = minFuelFlow / maxFuelFlow * 100d;
            if (minFuelFlow == maxFuelFlow)
                throttleP = 100d;
            if (!throttleLocked)
            {
                if (throttleP > 0d && throttleP < 100d)
                    output += ", " + throttleP.ToString("N0") + "% min throttle";
                else if(throttleP == 100d)
                    output += ", unthrottleable";
            }
            else
                output += ", throttle locked";

            return output;
        }
        protected string GetThrustInfo()
        {
            string output = "";
            if (engineSolver == null || !(engineSolver is SolverRF))
                CreateEngine();

            // get stats
            double pressure = 101.325d, temperature = 288.15d, density = 1.225d;
            if (Planetarium.fetch != null)
            {
                CelestialBody home = Planetarium.fetch.Home;
                if (home != null)
                {
                    pressure = home.GetPressure(0d);
                    temperature = home.GetTemperature(0d);
                    density = home.GetDensity(pressure, temperature);
                }
            }
            ambientTherm = new EngineThermodynamics();
            ambientTherm.FromAmbientConditions(pressure, temperature, density);
            inletTherm = new EngineThermodynamics();
            inletTherm.CopyFrom(ambientTherm);

            currentThrottle = 1f;
            lastPropellantFraction = 1d;
            bool oldE = EngineIgnited;
            EngineIgnited = true;
            rfSolver.UpdateThrustRatio(1d);

            UpdateFlightCondition(ambientTherm, 0d, Vector3d.zero, 0d, true);
            double thrustASL = (engineSolver.GetThrust() * 0.001d);

            if (atmChangeFlow) // If it's a jet
            {
                output += "<b>Static Thrust: </b>" + (thrustASL).ToString("0.0##") + " kN" + ThrottleString();
                if (useVelCurve) // if thrust changes with mach
                {
                    float vMin, vMax, tMin, tMax;
                    velCurve.FindMinMaxValue(out vMin, out vMax, out tMin, out tMax); // get the max mult, and thus report maximum thrust possible.
                    output += "\n<b>Max. Thrust: </b>" + (thrustASL* vMax).ToString("0.0##") + " kN Mach " + tMax.ToString("0.#");
                }
            }
            else
            {
                // get stats again
                double spaceHeight = 131000d;
                pressure = 0d;
                density = 0d;
                if (Planetarium.fetch != null)
                {
                    CelestialBody home = Planetarium.fetch.Home;
                    if (home != null)
                    {
                        temperature = home.GetTemperature(home.atmosphereDepth + 1d);
                        spaceHeight = home.atmosphereDepth + 1000d;
                    }
                }
                else
                    temperature = PhysicsGlobals.SpaceTemperature;
                ambientTherm.FromAmbientConditions(pressure, temperature, density);

                UpdateFlightCondition(ambientTherm, spaceHeight, Vector3d.zero, 0d, true);
                double thrustVac = (engineSolver.GetThrust() * 0.001d);

                if (thrustASL != thrustVac)
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (Vac.): </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString()
                        + "\n" + (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust (ASL): </b>" + (thrustASL).ToString("0.0##") + " kN";
                }
                else
                {
                    output += (throttleLocked ? "<b>" : "<b>Max. ") + "Thrust: </b>" + (thrustVac).ToString("0.0##") + " kN" + ThrottleString();
                }
            }
            output += "\n";
            EngineIgnited = oldE;
            return output;
        }

        public override string GetModuleTitle()
        {
            return "Engine (RealFuels)";
        }
        public override string GetPrimaryField()
        {
            return GetThrustInfo();
        }

        public override string GetInfo()
        {
            string output = GetThrustInfo();

            output += "<b>Engine Isp: </b>" + (atmosphereCurve.Evaluate(1f)).ToString("0.###") + " (ASL) - " + (atmosphereCurve.Evaluate(0f)).ToString("0.###") + " (Vac.)\n";

            output += "\n<b><color=#99ff00ff>Propellants:</color></b>\n";
            Propellant p;
            string pName;
            for (int i = 0; i < propellants.Count; ++i)
            {
                p = propellants[i];
                pName = KSPUtil.PrintModuleName(p.name);
                string units = "L";
                if(p.name == "ElectricCharger")
                    units = "kW";
                float unitsSec = getMaxFuelFlow(p);
                string unitsUsed = unitsSec.ToString("N4") + units;
                if (PartResourceLibrary.Instance != null)
                {
                    PartResourceDefinition def = PartResourceLibrary.Instance.GetDefinition(p.name);
                    if (def != null && def.density > 0)
                        unitsUsed += " (" + (unitsSec * def.density * 1000f).ToString("N4") + " kg)";
                }
                unitsUsed += " per second";
                output += "- <b>" + pName + "</b>: " + unitsUsed + " maximum.\n";
                output += p.GetFlowModeDescription();
            }
            output += "<b>Flameout under: </b>" + (ignitionThreshold * 100f).ToString("0.#") + "% of requirement remaining.\n";

            if (!allowShutdown) output += "\n" + "<b><color=orange>Engine cannot be shut down!</color></b>";
            if (!allowRestart) output += "\n" + "<b><color=orange>If shutdown, engine cannot restart.</color></b>";

            currentThrottle = 0f;

            return output;
        }
        #endregion
    }
}
