﻿/*
 * Copyright (c) Contributors, http://opensimulator.org/
 * See CONTRIBUTORS.TXT for a full list of copyright holders.
 *
 * Redistribution and use in source and binary forms, with or without
 * modification, are permitted provided that the following conditions are met:
 *     * Redistributions of source code must retain the above copyright
 *       notice, this list of conditions and the following disclaimer.
 *     * Redistributions in binary form must reproduce the above copyrightD
 *       notice, this list of conditions and the following disclaimer in the
 *       documentation and/or other materials provided with the distribution.
 *     * Neither the name of the OpenSimulator Project nor the
 *       names of its contributors may be used to endorse or promote products
 *       derived from this software without specific prior written permission.
 *
 * THIS SOFTWARE IS PROVIDED BY THE DEVELOPERS ``AS IS'' AND ANY
 * EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
 * WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
 * DISCLAIMED. IN NO EVENT SHALL THE CONTRIBUTORS BE LIABLE FOR ANY
 * DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
 * (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
 * LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
 * ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
 * (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
 * SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
 */

using System;
using System.Reflection;
using System.Collections.Generic;
using System.Xml;
using log4net;
using OMV = OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;
using OpenSim.Region.Physics.ConvexDecompositionDotNet;

namespace OpenSim.Region.Physics.BulletSPlugin
{

    [Serializable]
public class BSPrim : BSPhysObject
{
    private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly string LogHeader = "[BULLETS PRIM]";

    // _size is what the user passed. Scale is what we pass to the physics engine with the mesh.
    private OMV.Vector3 _size;  // the multiplier for each mesh dimension as passed by the user

    private bool _grabbed;
    private bool _isSelected;
    private bool _isVolumeDetect;

    // _position is what the simulator thinks the positions of the prim is.
    private OMV.Vector3 _position;

    private float _mass;    // the mass of this object
    private OMV.Vector3 _force;
    private OMV.Vector3 _velocity;
    private OMV.Vector3 _torque;
    private OMV.Vector3 _acceleration;
    private OMV.Quaternion _orientation;
    private int _physicsActorType;
    private bool _isPhysical;
    private bool _flying;
    private bool _setAlwaysRun;
    private bool _throttleUpdates;
    private bool _floatOnWater;
    private OMV.Vector3 _rotationalVelocity;
    private bool _kinematic;
    private float _buoyancy;

    private int CrossingFailures { get; set; }

    public BSDynamics VehicleController { get; private set; }

    private BSVMotor _targetMotor;
    private OMV.Vector3 _PIDTarget;
    private float _PIDTau;

    private BSFMotor _hoverMotor;
    private float _PIDHoverHeight;
    private PIDHoverType _PIDHoverType;
    private float _PIDHoverTau;

    public BSPrim(uint localID, String primName, BSScene parent_scene, OMV.Vector3 pos, OMV.Vector3 size,
                       OMV.Quaternion rotation, PrimitiveBaseShape pbs, bool pisPhysical)
            : base(parent_scene, localID, primName, "BSPrim")
    {
        // m_log.DebugFormat("{0}: BSPrim creation of {1}, id={2}", LogHeader, primName, localID);
        _physicsActorType = (int)ActorTypes.Prim;
        _position = pos;
        _size = size;
        Scale = size;   // prims are the size the user wants them to be (different for BSCharactes).
        _orientation = rotation;
        _buoyancy = 0f;
        _velocity = OMV.Vector3.Zero;
        _rotationalVelocity = OMV.Vector3.Zero;
        BaseShape = pbs;
        _isPhysical = pisPhysical;
        _isVolumeDetect = false;

        VehicleController = new BSDynamics(PhysicsScene, this);            // add vehicleness

        _mass = CalculateMass();

        DetailLog("{0},BSPrim.constructor,call", LocalID);
        // do the actual object creation at taint time
        PhysicsScene.TaintedObject("BSPrim.create", delegate()
        {
            // Make sure the object is being created with some sanity.
            ExtremeSanityCheck(true /* inTaintTime */);

            CreateGeomAndObject(true);

            CurrentCollisionFlags = PhysicsScene.PE.GetCollisionFlags(PhysBody);
        });
    }

    // called when this prim is being destroyed and we should free all the resources
    public override void Destroy()
    {
        // m_log.DebugFormat("{0}: Destroy, id={1}", LogHeader, LocalID);
        base.Destroy();

        // Undo any vehicle properties
        this.VehicleType = (int)Vehicle.TYPE_NONE;

        PhysicsScene.TaintedObject("BSPrim.destroy", delegate()
        {
            DetailLog("{0},BSPrim.Destroy,taint,", LocalID);
            // If there are physical body and shape, release my use of same.
            PhysicsScene.Shapes.DereferenceBody(PhysBody, null);
            PhysBody.Clear();
            PhysicsScene.Shapes.DereferenceShape(PhysShape, null);
            PhysShape.Clear();
        });
    }

    // No one uses this property.
    public override bool Stopped {
        get { return false; }
    }
    public override OMV.Vector3 Size {
        get { return _size; }
        set {
            // We presume the scale and size are the same. If scale must be changed for
            //     the physical shape, that is done when the geometry is built.
            _size = value;
            Scale = _size;
            ForceBodyShapeRebuild(false);
        }
    }

    public override PrimitiveBaseShape Shape {
        set {
            BaseShape = value;
            LastAssetBuildFailed = false;
            ForceBodyShapeRebuild(false);
        }
    }
    // 'unknown' says to choose the best type
    public override BSPhysicsShapeType PreferredPhysicalShape
        { get { return BSPhysicsShapeType.SHAPE_UNKNOWN; } }

    public override bool ForceBodyShapeRebuild(bool inTaintTime)
    {
        if (inTaintTime)
        {
            _mass = CalculateMass();   // changing the shape changes the mass
            CreateGeomAndObject(true);
        }
        else
        {
            PhysicsScene.TaintedObject("BSPrim.ForceBodyShapeRebuild", delegate()
            {
                _mass = CalculateMass();   // changing the shape changes the mass
                CreateGeomAndObject(true);
            });
        }
        return true;
    }
    public override bool Grabbed {
        set { _grabbed = value;
        }
    }
    public override bool Selected {
        set
        {
            if (value != _isSelected)
            {
                _isSelected = value;
                PhysicsScene.TaintedObject("BSPrim.setSelected", delegate()
                {
                    DetailLog("{0},BSPrim.selected,taint,selected={1}", LocalID, _isSelected);
                    SetObjectDynamic(false);
                });
            }
        }
    }
    public override bool IsSelected
    {
        get { return _isSelected; }
    }

    public override void CrossingFailure()
    {
        CrossingFailures++;
        if (CrossingFailures > BSParam.CrossingFailuresBeforeOutOfBounds)
        {
            base.RaiseOutOfBounds(RawPosition);
        }
        else if (CrossingFailures == BSParam.CrossingFailuresBeforeOutOfBounds)
        {
            m_log.WarnFormat("{0} Too many crossing failures for {1}", LogHeader, Name);
        }
        return;
    }

    // link me to the specified parent
    public override void link(PhysicsActor obj) {
    }

    // delink me from my linkset
    public override void delink() {
    }

    // Set motion values to zero.
    // Do it to the properties so the values get set in the physics engine.
    // Push the setting of the values to the viewer.
    // Called at taint time!
    public override void ZeroMotion(bool inTaintTime)
    {
        _velocity = OMV.Vector3.Zero;
        _acceleration = OMV.Vector3.Zero;
        _rotationalVelocity = OMV.Vector3.Zero;

        // Zero some other properties in the physics engine
        PhysicsScene.TaintedObject(inTaintTime, "BSPrim.ZeroMotion", delegate()
        {
            if (PhysBody.HasPhysicalBody)
                PhysicsScene.PE.ClearAllForces(PhysBody);
        });
    }
    public override void ZeroAngularMotion(bool inTaintTime)
    {
        _rotationalVelocity = OMV.Vector3.Zero;
        // Zero some other properties in the physics engine
        PhysicsScene.TaintedObject(inTaintTime, "BSPrim.ZeroMotion", delegate()
        {
            // DetailLog("{0},BSPrim.ZeroAngularMotion,call,rotVel={1}", LocalID, _rotationalVelocity);
            if (PhysBody.HasPhysicalBody)
            {
                PhysicsScene.PE.SetInterpolationAngularVelocity(PhysBody, _rotationalVelocity);
                PhysicsScene.PE.SetAngularVelocity(PhysBody, _rotationalVelocity);
            }
        });
    }

    bool TryExperimentalLockAxisCode = false;
    BSConstraint LockAxisConstraint = null;
    public override void LockAngularMotion(OMV.Vector3 axis)
    {
        DetailLog("{0},BSPrim.LockAngularMotion,call,axis={1}", LocalID, axis);

        // "1" means free, "0" means locked
        OMV.Vector3 locking = new OMV.Vector3(1f, 1f, 1f);
        if (axis.X != 1) locking.X = 0f;
        if (axis.Y != 1) locking.Y = 0f;
        if (axis.Z != 1) locking.Z = 0f;
        LockedAxis = locking;

        if (TryExperimentalLockAxisCode && LockedAxis != LockedAxisFree)
        {
            // Lock that axis by creating a 6DOF constraint that has one end in the world and
            //    the other in the object.
            // http://www.bulletphysics.org/Bullet/phpBB3/viewtopic.php?p=20817
            // http://www.bulletphysics.org/Bullet/phpBB3/viewtopic.php?p=26380

            PhysicsScene.TaintedObject("BSPrim.LockAngularMotion", delegate()
            {
                CleanUpLockAxisPhysicals(true /* inTaintTime */);

                BSConstraint6Dof axisConstrainer = new BSConstraint6Dof(PhysicsScene.World, PhysBody, 
                                    OMV.Vector3.Zero, OMV.Quaternion.Inverse(RawOrientation),
                                    true /* useLinearReferenceFrameB */, true /* disableCollisionsBetweenLinkedBodies */);
                LockAxisConstraint = axisConstrainer;
                PhysicsScene.Constraints.AddConstraint(LockAxisConstraint);

                // The constraint is tied to the world and oriented to the prim.

                // Free to move linearly
                OMV.Vector3 linearLow = OMV.Vector3.Zero;
                OMV.Vector3 linearHigh = PhysicsScene.TerrainManager.DefaultRegionSize;
                axisConstrainer.SetLinearLimits(linearLow, linearHigh);

                // Angular with some axis locked
                float f2PI = (float)Math.PI * 2f;
                OMV.Vector3 angularLow = new OMV.Vector3(-f2PI, -f2PI, -f2PI);
                OMV.Vector3 angularHigh = new OMV.Vector3(f2PI, f2PI, f2PI);
                if (LockedAxis.X != 1f)
                {
                    angularLow.X = 0f;
                    angularHigh.X = 0f;
                }
                if (LockedAxis.Y != 1f)
                {
                    angularLow.Y = 0f;
                    angularHigh.Y = 0f;
                }
                if (LockedAxis.Z != 1f)
                {
                    angularLow.Z = 0f;
                    angularHigh.Z = 0f;
                }
                axisConstrainer.SetAngularLimits(angularLow, angularHigh);

                DetailLog("{0},BSPrim.LockAngularMotion,create,linLow={1},linHi={2},angLow={3},angHi={4}",
                                            LocalID, linearLow, linearHigh, angularLow, angularHigh);

                // Constants from one of the posts mentioned above and used in Bullet's ConstraintDemo.
                axisConstrainer.TranslationalLimitMotor(true /* enable */, 5.0f, 0.1f);

                axisConstrainer.RecomputeConstraintVariables(RawMass);
            });
        }
        else
        {
            // Everything seems unlocked
            CleanUpLockAxisPhysicals(false /* inTaintTime */);
        }

        return;
    }
    // Get rid of any constraint built for LockAxis
    // Most often the constraint is removed when the constraint collection is cleaned for this prim.
    private void CleanUpLockAxisPhysicals(bool inTaintTime)
    {
        if (LockAxisConstraint != null)
        {
            PhysicsScene.TaintedObject(inTaintTime, "BSPrim.CleanUpLockAxisPhysicals", delegate()
            {
                if (LockAxisConstraint != null)
                {
                    PhysicsScene.Constraints.RemoveAndDestroyConstraint(LockAxisConstraint);
                    LockAxisConstraint = null;
                    DetailLog("{0},BSPrim.CleanUpLockAxisPhysicals,destroyingConstraint", LocalID);
                }
            });
        }
    }

    public override OMV.Vector3 RawPosition
    {
        get { return _position; }
        set { _position = value; }
    }
    public override OMV.Vector3 Position {
        get {
            // don't do the GetObjectPosition for root elements because this function is called a zillion times.
            // _position = ForcePosition;
            return _position;
        }
        set {
            // If the position must be forced into the physics engine, use ForcePosition.
            // All positions are given in world positions.
            if (_position == value)
            {
                DetailLog("{0},BSPrim.setPosition,call,positionNotChanging,pos={1},orient={2}", LocalID, _position, _orientation);
                return;
            }
            _position = value;
            PositionSanityCheck(false);

            PhysicsScene.TaintedObject("BSPrim.setPosition", delegate()
            {
                DetailLog("{0},BSPrim.SetPosition,taint,pos={1},orient={2}", LocalID, _position, _orientation);
                ForcePosition = _position;
            });
        }
    }

    public override OMV.Vector3 ForcePosition {
        get {
            _position = PhysicsScene.PE.GetPosition(PhysBody);
            return _position;
        }
        set {
            _position = value;
            if (PhysBody.HasPhysicalBody)
            {
                PhysicsScene.PE.SetTranslation(PhysBody, _position, _orientation);
                ActivateIfPhysical(false);
            }
        }
    }

    // Check that the current position is sane and, if not, modify the position to make it so.
    // Check for being below terrain and being out of bounds.
    // Returns 'true' of the position was made sane by some action.
    private bool PositionSanityCheck(bool inTaintTime)
    {
        bool ret = false;

        // We don't care where non-physical items are placed
        if (!IsPhysicallyActive)
            return ret;

        if (!PhysicsScene.TerrainManager.IsWithinKnownTerrain(RawPosition))
        {
            // The physical object is out of the known/simulated area.
            // Upper levels of code will handle the transition to other areas so, for
            //     the time, we just ignore the position.
            return ret;
        }

        float terrainHeight = PhysicsScene.TerrainManager.GetTerrainHeightAtXYZ(RawPosition);
        OMV.Vector3 upForce = OMV.Vector3.Zero;
        float approxSize = Math.Max(Size.X, Math.Max(Size.Y, Size.Z));
        if ((RawPosition.Z + approxSize / 2f) < terrainHeight)
        {
            DetailLog("{0},BSPrim.PositionAdjustUnderGround,call,pos={1},terrain={2}", LocalID, RawPosition, terrainHeight);
            float targetHeight = terrainHeight + (Size.Z / 2f);
            // If the object is below ground it just has to be moved up because pushing will
            //     not get it through the terrain
            _position.Z = targetHeight;
            if (inTaintTime)
            {
                ForcePosition = _position;
            }
            // If we are throwing the object around, zero its other forces
            ZeroMotion(inTaintTime);
            ret = true;
        }

        if ((CurrentCollisionFlags & CollisionFlags.BS_FLOATS_ON_WATER) != 0)
        {
            float waterHeight = PhysicsScene.TerrainManager.GetWaterLevelAtXYZ(_position);
            // TODO: a floating motor so object will bob in the water
            if (Math.Abs(RawPosition.Z - waterHeight) > 0.1f)
            {
                // Upforce proportional to the distance away from the water. Correct the error in 1 sec.
                upForce.Z = (waterHeight - RawPosition.Z) * 1f;

                // Apply upforce and overcome gravity.
                OMV.Vector3 correctionForce = upForce - PhysicsScene.DefaultGravity;
                DetailLog("{0},BSPrim.PositionSanityCheck,applyForce,pos={1},upForce={2},correctionForce={3}", LocalID, _position, upForce, correctionForce);
                AddForce(correctionForce, false, inTaintTime);
                ret = true;
            }
        }

        return ret;
    }

    // Occasionally things will fly off and really get lost.
    // Find the wanderers and bring them back.
    // Return 'true' if some parameter need some sanity.
    private bool ExtremeSanityCheck(bool inTaintTime)
    {
        bool ret = false;

        uint wayOutThere = Constants.RegionSize * Constants.RegionSize;
        // There have been instances of objects getting thrown way out of bounds and crashing
        //    the border crossing code.
        if (   _position.X < -Constants.RegionSize || _position.X > wayOutThere
            || _position.Y < -Constants.RegionSize || _position.Y > wayOutThere
            || _position.Z < -Constants.RegionSize || _position.Z > wayOutThere)
        {
            _position = new OMV.Vector3(10, 10, 50);
            ZeroMotion(inTaintTime);
            ret = true;
        }
        if (_velocity.LengthSquared() > BSParam.MaxLinearVelocity)
        {
            _velocity = Util.ClampV(_velocity, BSParam.MaxLinearVelocity);
            ret = true;
        }
        if (_rotationalVelocity.LengthSquared() > BSParam.MaxAngularVelocitySquared)
        {
            _rotationalVelocity = Util.ClampV(_rotationalVelocity, BSParam.MaxAngularVelocity);
            ret = true;
        }

        return ret;
    }

    // Return the effective mass of the object.
        // The definition of this call is to return the mass of the prim.
        // If the simulator cares about the mass of the linkset, it will sum it itself.
    public override float Mass
    {
        get { return _mass; }
    }
    // TotalMass returns the mass of the large object the prim may be in (overridden by linkset code)
    public virtual float TotalMass
    {
        get { return _mass; }
    }
    // used when we only want this prim's mass and not the linkset thing
    public override float RawMass { 
        get { return _mass; }
    }
    // Set the physical mass to the passed mass.
    // Note that this does not change _mass!
    public override void UpdatePhysicalMassProperties(float physMass, bool inWorld)
    {
        if (PhysBody.HasPhysicalBody && PhysShape.HasPhysicalShape)
        {
            if (IsStatic)
            {
                PhysicsScene.PE.SetGravity(PhysBody, PhysicsScene.DefaultGravity);
                Inertia = OMV.Vector3.Zero;
                PhysicsScene.PE.SetMassProps(PhysBody, 0f, Inertia);
                PhysicsScene.PE.UpdateInertiaTensor(PhysBody);
            }
            else
            {
                if (inWorld)
                {
                    // Changing interesting properties doesn't change proxy and collision cache
                    //    information. The Bullet solution is to re-add the object to the world
                    //    after parameters are changed.
                    PhysicsScene.PE.RemoveObjectFromWorld(PhysicsScene.World, PhysBody);
                }

                // The computation of mass props requires gravity to be set on the object.
                Gravity = ComputeGravity(Buoyancy);
                PhysicsScene.PE.SetGravity(PhysBody, Gravity);

                Inertia = PhysicsScene.PE.CalculateLocalInertia(PhysShape, physMass);
                PhysicsScene.PE.SetMassProps(PhysBody, physMass, Inertia);
                PhysicsScene.PE.UpdateInertiaTensor(PhysBody);

                DetailLog("{0},BSPrim.UpdateMassProperties,mass={1},localInertia={2},grav={3},inWorld={4}",
                                            LocalID, physMass, Inertia, Gravity, inWorld);

                if (inWorld)
                {
                    AddObjectToPhysicalWorld();
                }
            }
        }
    }

    // Return what gravity should be set to this very moment
    public OMV.Vector3 ComputeGravity(float buoyancy)
    {
        OMV.Vector3 ret = PhysicsScene.DefaultGravity;

        if (!IsStatic)
        {
            ret *= (1f - buoyancy);
            ret *= GravModifier;
        }

        return ret;
    }

    // Is this used?
    public override OMV.Vector3 CenterOfMass
    {
        get { return RawPosition; }
    }

    // Is this used?
    public override OMV.Vector3 GeometricCenter
    {
        get { return RawPosition; }
    }

    public override OMV.Vector3 Force {
        get { return _force; }
        set {
            _force = value;
            if (_force != OMV.Vector3.Zero)
            {
                // If the force is non-zero, it must be reapplied each tick because
                //    Bullet clears the forces applied last frame.
                RegisterPreStepAction("BSPrim.setForce", LocalID,
                    delegate(float timeStep)
                    {
                        if (!IsPhysicallyActive || _force == OMV.Vector3.Zero)
                        {
                            UnRegisterPreStepAction("BSPrim.setForce", LocalID);
                            return;
                        }

                        DetailLog("{0},BSPrim.setForce,preStep,force={1}", LocalID, _force);
                        if (PhysBody.HasPhysicalBody)
                        {
                            PhysicsScene.PE.ApplyCentralForce(PhysBody, _force);
                            ActivateIfPhysical(false);
                        }
                    }
                );
            }
            else
            {
                UnRegisterPreStepAction("BSPrim.setForce", LocalID);
            }
        }
    }

    public override int VehicleType {
        get {
            return (int)VehicleController.Type;   // if we are a vehicle, return that type
        }
        set {
            Vehicle type = (Vehicle)value;

            PhysicsScene.TaintedObject("setVehicleType", delegate()
            {
                // Done at taint time so we're sure the physics engine is not using the variables
                // Vehicle code changes the parameters for this vehicle type.
                VehicleController.ProcessTypeChange(type);
                ActivateIfPhysical(false);

                // If an active vehicle, register the vehicle code to be called before each step
                if (VehicleController.Type == Vehicle.TYPE_NONE)
                {
                    UnRegisterPreStepAction("BSPrim.Vehicle", LocalID);
                    UnRegisterPostStepAction("BSPrim.Vehicle", LocalID);
                }
                else
                {
                    RegisterPreStepAction("BSPrim.Vehicle", LocalID, VehicleController.Step);
                    RegisterPostStepAction("BSPrim.Vehicle", LocalID, VehicleController.PostStep);
                }
            });
        }
    }
    public override void VehicleFloatParam(int param, float value)
    {
        PhysicsScene.TaintedObject("BSPrim.VehicleFloatParam", delegate()
        {
            VehicleController.ProcessFloatVehicleParam((Vehicle)param, value);
            ActivateIfPhysical(false);
        });
    }
    public override void VehicleVectorParam(int param, OMV.Vector3 value)
    {
        PhysicsScene.TaintedObject("BSPrim.VehicleVectorParam", delegate()
        {
            VehicleController.ProcessVectorVehicleParam((Vehicle)param, value);
            ActivateIfPhysical(false);
        });
    }
    public override void VehicleRotationParam(int param, OMV.Quaternion rotation)
    {
        PhysicsScene.TaintedObject("BSPrim.VehicleRotationParam", delegate()
        {
            VehicleController.ProcessRotationVehicleParam((Vehicle)param, rotation);
            ActivateIfPhysical(false);
        });
    }
    public override void VehicleFlags(int param, bool remove)
    {
        PhysicsScene.TaintedObject("BSPrim.VehicleFlags", delegate()
        {
            VehicleController.ProcessVehicleFlags(param, remove);
        });
    }

    // Allows the detection of collisions with inherently non-physical prims. see llVolumeDetect for more
    public override void SetVolumeDetect(int param) {
        bool newValue = (param != 0);
        if (_isVolumeDetect != newValue)
        {
            _isVolumeDetect = newValue;
            PhysicsScene.TaintedObject("BSPrim.SetVolumeDetect", delegate()
            {
                // DetailLog("{0},setVolumeDetect,taint,volDetect={1}", LocalID, _isVolumeDetect);
                SetObjectDynamic(true);
            });
        }
        return;
    }
    public override void SetMaterial(int material)
    {
        base.SetMaterial(material);
        PhysicsScene.TaintedObject("BSPrim.SetMaterial", delegate()
        {
            UpdatePhysicalParameters();
        });
    }
    public override float Friction
    {
        get { return base.Friction; }
        set
        {
            if (base.Friction != value)
            {
                base.Friction = value;
                PhysicsScene.TaintedObject("BSPrim.setFriction", delegate()
                {
                    UpdatePhysicalParameters();
                });
            }
        }
    }
    public override float Restitution
    {
        get { return base.Restitution; }
        set
        {
            if (base.Restitution != value)
            {
                base.Restitution = value;
                PhysicsScene.TaintedObject("BSPrim.setRestitution", delegate()
                {
                    UpdatePhysicalParameters();
                });
            }
        }
    }
    // The simulator/viewer keep density as 100kg/m3.
    // Remember to use BSParam.DensityScaleFactor to create the physical density.
    public override float Density
    {
        get { return base.Density; }
        set
        {
            if (base.Density != value)
            {
                base.Density = value;
                PhysicsScene.TaintedObject("BSPrim.setDensity", delegate()
                {
                    UpdatePhysicalParameters();
                });
            }
        }
    }
    public override float GravModifier
    {
        get { return base.GravModifier; }
        set
        {
            if (base.GravModifier != value)
            {
                base.GravModifier = value;
                PhysicsScene.TaintedObject("BSPrim.setGravityModifier", delegate()
                {
                    UpdatePhysicalParameters();
                });
            }
        }
    }
    public override OMV.Vector3 RawVelocity
    {
        get { return _velocity; }
        set { _velocity = value; }
    }
    public override OMV.Vector3 Velocity {
        get { return _velocity; }
        set {
            _velocity = value;
            PhysicsScene.TaintedObject("BSPrim.setVelocity", delegate()
            {
                // DetailLog("{0},BSPrim.SetVelocity,taint,vel={1}", LocalID, _velocity);
                ForceVelocity = _velocity;
            });
        }
    }
    public override OMV.Vector3 ForceVelocity {
        get { return _velocity; }
        set {
            PhysicsScene.AssertInTaintTime("BSPrim.ForceVelocity");

            _velocity = Util.ClampV(value, BSParam.MaxLinearVelocity);
            if (PhysBody.HasPhysicalBody)
            {
                DetailLog("{0},BSPrim.ForceVelocity,taint,vel={1}", LocalID, _velocity);
                PhysicsScene.PE.SetLinearVelocity(PhysBody, _velocity);
                ActivateIfPhysical(false);
            }
        }
    }
    public override OMV.Vector3 Torque {
        get { return _torque; }
        set {
            _torque = value;
            if (_torque != OMV.Vector3.Zero)
            {
                // If the torque is non-zero, it must be reapplied each tick because
                //    Bullet clears the forces applied last frame.
                RegisterPreStepAction("BSPrim.setTorque", LocalID,
                    delegate(float timeStep)
                    {
                        if (!IsPhysicallyActive || _torque == OMV.Vector3.Zero)
                        {
                            UnRegisterPreStepAction("BSPrim.setTorque", LocalID);
                            return;
                        }

                        if (PhysBody.HasPhysicalBody)
                            AddAngularForce(_torque, false, true);
                    }
                );
            }
            else
            {
                UnRegisterPreStepAction("BSPrim.setTorque", LocalID);
            }
            // DetailLog("{0},BSPrim.SetTorque,call,torque={1}", LocalID, _torque);
        }
    }
    public override OMV.Vector3 Acceleration {
        get { return _acceleration; }
        set { _acceleration = value; }
    }
    public override OMV.Quaternion RawOrientation
    {
        get { return _orientation; }
        set { _orientation = value; }
    }
    public override OMV.Quaternion Orientation {
        get {
            return _orientation;
        }
        set {
            if (_orientation == value)
                return;
            _orientation = value;

            PhysicsScene.TaintedObject("BSPrim.setOrientation", delegate()
            {
                ForceOrientation = _orientation;
            });
        }
    }
    // Go directly to Bullet to get/set the value.
    public override OMV.Quaternion ForceOrientation
    {
        get
        {
            _orientation = PhysicsScene.PE.GetOrientation(PhysBody);
            return _orientation;
        }
        set
        {
            _orientation = value;
            if (PhysBody.HasPhysicalBody)
                PhysicsScene.PE.SetTranslation(PhysBody, _position, _orientation);
        }
    }
    public override int PhysicsActorType {
        get { return _physicsActorType; }
        set { _physicsActorType = value; }
    }
    public override bool IsPhysical {
        get { return _isPhysical; }
        set {
            if (_isPhysical != value)
            {
                _isPhysical = value;
                PhysicsScene.TaintedObject("BSPrim.setIsPhysical", delegate()
                {
                    DetailLog("{0},setIsPhysical,taint,isPhys={1}", LocalID, _isPhysical);
                    SetObjectDynamic(true);
                    // whether phys-to-static or static-to-phys, the object is not moving.
                    ZeroMotion(true);

                });
            }
        }
    }

    // An object is static (does not move) if selected or not physical
    public override bool IsStatic
    {
        get { return _isSelected || !IsPhysical; }
    }

    // An object is solid if it's not phantom and if it's not doing VolumeDetect
    public override bool IsSolid
    {
        get { return !IsPhantom && !_isVolumeDetect; }
    }

    // The object is moving and is actively being dynamic in the physical world
    public override bool IsPhysicallyActive
    {
        get { return !_isSelected && IsPhysical; }
    }

    // Make gravity work if the object is physical and not selected
    // Called at taint-time!!
    private void SetObjectDynamic(bool forceRebuild)
    {
        // Recreate the physical object if necessary
        CreateGeomAndObject(forceRebuild);
    }

    // Convert the simulator's physical properties into settings on BulletSim objects.
    // There are four flags we're interested in:
    //     IsStatic: Object does not move, otherwise the object has mass and moves
    //     isSolid: other objects bounce off of this object
    //     isVolumeDetect: other objects pass through but can generate collisions
    //     collisionEvents: whether this object returns collision events
    public virtual void UpdatePhysicalParameters()
    {
        if (!PhysBody.HasPhysicalBody)
        {
            // This would only happen if updates are called for during initialization when the body is not set up yet.
            DetailLog("{0},BSPrim.UpdatePhysicalParameters,taint,calledWithNoPhysBody", LocalID);
            return;
        }

        // Mangling all the physical properties requires the object not be in the physical world.
        // This is a NOOP if the object is not in the world (BulletSim and Bullet ignore objects not found).
        PhysicsScene.PE.RemoveObjectFromWorld(PhysicsScene.World, PhysBody);

        // Set up the object physicalness (does gravity and collisions move this object)
        MakeDynamic(IsStatic);

        // Update vehicle specific parameters (after MakeDynamic() so can change physical parameters)
        VehicleController.Refresh();

        // Arrange for collision events if the simulator wants them
        EnableCollisions(SubscribedEvents());

        // Make solid or not (do things bounce off or pass through this object).
        MakeSolid(IsSolid);

        AddObjectToPhysicalWorld();

        // Rebuild its shape
        PhysicsScene.PE.UpdateSingleAabb(PhysicsScene.World, PhysBody);

        DetailLog("{0},BSPrim.UpdatePhysicalParameters,taintExit,static={1},solid={2},mass={3},collide={4},cf={5:X},cType={6},body={7},shape={8}",
                        LocalID, IsStatic, IsSolid, Mass, SubscribedEvents(), CurrentCollisionFlags, PhysBody.collisionType, PhysBody, PhysShape);
    }

    // "Making dynamic" means changing to and from static.
    // When static, gravity does not effect the object and it is fixed in space.
    // When dynamic, the object can fall and be pushed by others.
    // This is independent of its 'solidness' which controls what passes through
    //    this object and what interacts with it.
    protected virtual void MakeDynamic(bool makeStatic)
    {
        if (makeStatic)
        {
            // Become a Bullet 'static' object type
            CurrentCollisionFlags = PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.CF_STATIC_OBJECT);
            // Stop all movement
            ZeroMotion(true);

            // Set various physical properties so other object interact properly
            MaterialAttributes matAttrib = BSMaterials.GetAttributes(Material, false);
            PhysicsScene.PE.SetFriction(PhysBody, Friction);
            PhysicsScene.PE.SetRestitution(PhysBody, Restitution);

            // Mass is zero which disables a bunch of physics stuff in Bullet
            UpdatePhysicalMassProperties(0f, false);
            // Set collision detection parameters
            if (BSParam.CcdMotionThreshold > 0f)
            {
                PhysicsScene.PE.SetCcdMotionThreshold(PhysBody, BSParam.CcdMotionThreshold);
                PhysicsScene.PE.SetCcdSweptSphereRadius(PhysBody, BSParam.CcdSweptSphereRadius);
            }

            // The activation state is 'disabled' so Bullet will not try to act on it.
            // PhysicsScene.PE.ForceActivationState(PhysBody, ActivationState.DISABLE_SIMULATION);
            // Start it out sleeping and physical actions could wake it up.
            PhysicsScene.PE.ForceActivationState(PhysBody, ActivationState.ISLAND_SLEEPING);

            // This collides like a static object
            PhysBody.collisionType = CollisionType.Static;
        }
        else
        {
            // Not a Bullet static object
            CurrentCollisionFlags = PhysicsScene.PE.RemoveFromCollisionFlags(PhysBody, CollisionFlags.CF_STATIC_OBJECT);

            // Set various physical properties so other object interact properly
            PhysicsScene.PE.SetFriction(PhysBody, Friction);
            PhysicsScene.PE.SetRestitution(PhysBody, Restitution);
            // DetailLog("{0},BSPrim.MakeDynamic,frict={1},rest={2}", LocalID, Friction, Restitution);

            // per http://www.bulletphysics.org/Bullet/phpBB3/viewtopic.php?t=3382
            // Since this can be called multiple times, only zero forces when becoming physical
            // PhysicsScene.PE.ClearAllForces(BSBody);

            // For good measure, make sure the transform is set through to the motion state
            ForcePosition = _position;
            ForceVelocity = _velocity;
            ForceRotationalVelocity = _rotationalVelocity;

            // A dynamic object has mass
            UpdatePhysicalMassProperties(RawMass, false);

            // Set collision detection parameters
            if (BSParam.CcdMotionThreshold > 0f)
            {
                PhysicsScene.PE.SetCcdMotionThreshold(PhysBody, BSParam.CcdMotionThreshold);
                PhysicsScene.PE.SetCcdSweptSphereRadius(PhysBody, BSParam.CcdSweptSphereRadius);
            }

            // Various values for simulation limits
            PhysicsScene.PE.SetDamping(PhysBody, BSParam.LinearDamping, BSParam.AngularDamping);
            PhysicsScene.PE.SetDeactivationTime(PhysBody, BSParam.DeactivationTime);
            PhysicsScene.PE.SetSleepingThresholds(PhysBody, BSParam.LinearSleepingThreshold, BSParam.AngularSleepingThreshold);
            PhysicsScene.PE.SetContactProcessingThreshold(PhysBody, BSParam.ContactProcessingThreshold);

            // This collides like an object.
            PhysBody.collisionType = CollisionType.Dynamic;

            // Force activation of the object so Bullet will act on it.
            // Must do the ForceActivationState2() to overcome the DISABLE_SIMULATION from static objects.
            PhysicsScene.PE.ForceActivationState(PhysBody, ActivationState.ACTIVE_TAG);
        }
    }

    // "Making solid" means that other object will not pass through this object.
    // To make transparent, we create a Bullet ghost object.
    // Note: This expects to be called from the UpdatePhysicalParameters() routine as
    //     the functions after this one set up the state of a possibly newly created collision body.
    private void MakeSolid(bool makeSolid)
    {
        CollisionObjectTypes bodyType = (CollisionObjectTypes)PhysicsScene.PE.GetBodyType(PhysBody);
        if (makeSolid)
        {
            // Verify the previous code created the correct shape for this type of thing.
            if ((bodyType & CollisionObjectTypes.CO_RIGID_BODY) == 0)
            {
                m_log.ErrorFormat("{0} MakeSolid: physical body of wrong type for solidity. id={1}, type={2}", LogHeader, LocalID, bodyType);
            }
            CurrentCollisionFlags = PhysicsScene.PE.RemoveFromCollisionFlags(PhysBody, CollisionFlags.CF_NO_CONTACT_RESPONSE);
        }
        else
        {
            if ((bodyType & CollisionObjectTypes.CO_GHOST_OBJECT) == 0)
            {
                m_log.ErrorFormat("{0} MakeSolid: physical body of wrong type for non-solidness. id={1}, type={2}", LogHeader, LocalID, bodyType);
            }
            CurrentCollisionFlags = PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.CF_NO_CONTACT_RESPONSE);

            // Change collision info from a static object to a ghosty collision object
            PhysBody.collisionType = CollisionType.VolumeDetect;
        }
    }

    // Enable physical actions. Bullet will keep sleeping non-moving physical objects so
    //     they need waking up when parameters are changed.
    // Called in taint-time!!
    private void ActivateIfPhysical(bool forceIt)
    {
        if (IsPhysical && PhysBody.HasPhysicalBody)
            PhysicsScene.PE.Activate(PhysBody, forceIt);
    }

    // Turn on or off the flag controlling whether collision events are returned to the simulator.
    private void EnableCollisions(bool wantsCollisionEvents)
    {
        if (wantsCollisionEvents)
        {
            CurrentCollisionFlags = PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.BS_SUBSCRIBE_COLLISION_EVENTS);
        }
        else
        {
            CurrentCollisionFlags = PhysicsScene.PE.RemoveFromCollisionFlags(PhysBody, CollisionFlags.BS_SUBSCRIBE_COLLISION_EVENTS);
        }
    }

    // Add me to the physical world.
    // Object MUST NOT already be in the world.
    // This routine exists because some assorted properties get mangled by adding to the world.
    internal void AddObjectToPhysicalWorld()
    {
        if (PhysBody.HasPhysicalBody)
        {
            PhysicsScene.PE.AddObjectToWorld(PhysicsScene.World, PhysBody);
        }
        else
        {
            m_log.ErrorFormat("{0} Attempt to add physical object without body. id={1}", LogHeader, LocalID);
            DetailLog("{0},BSPrim.AddObjectToPhysicalWorld,addObjectWithoutBody,cType={1}", LocalID, PhysBody.collisionType);
        }
    }

    // prims don't fly
    public override bool Flying {
        get { return _flying; }
        set {
            _flying = value;
        }
    }
    public override bool SetAlwaysRun {
        get { return _setAlwaysRun; }
        set { _setAlwaysRun = value; }
    }
    public override bool ThrottleUpdates {
        get { return _throttleUpdates; }
        set { _throttleUpdates = value; }
    }
    public bool IsPhantom {
        get {
            // SceneObjectPart removes phantom objects from the physics scene
            // so, although we could implement touching and such, we never
            // are invoked as a phantom object
            return false;
        }
    }
    public override bool FloatOnWater {
        set {
            _floatOnWater = value;
            PhysicsScene.TaintedObject("BSPrim.setFloatOnWater", delegate()
            {
                if (_floatOnWater)
                    CurrentCollisionFlags = PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.BS_FLOATS_ON_WATER);
                else
                    CurrentCollisionFlags = PhysicsScene.PE.RemoveFromCollisionFlags(PhysBody, CollisionFlags.BS_FLOATS_ON_WATER);
            });
        }
    }
    public override OMV.Vector3 RotationalVelocity {
        get {
            return _rotationalVelocity;
        }
        set {
            _rotationalVelocity = value;
            Util.ClampV(_rotationalVelocity, BSParam.MaxAngularVelocity);
            // m_log.DebugFormat("{0}: RotationalVelocity={1}", LogHeader, _rotationalVelocity);
            PhysicsScene.TaintedObject("BSPrim.setRotationalVelocity", delegate()
            {
                ForceRotationalVelocity = _rotationalVelocity;
            });
        }
    }
    public override OMV.Vector3 ForceRotationalVelocity {
        get {
            return _rotationalVelocity;
        }
        set {
            _rotationalVelocity = Util.ClampV(value, BSParam.MaxAngularVelocity);
            if (PhysBody.HasPhysicalBody)
            {
                DetailLog("{0},BSPrim.ForceRotationalVel,taint,rotvel={1}", LocalID, _rotationalVelocity);
                PhysicsScene.PE.SetAngularVelocity(PhysBody, _rotationalVelocity);
                // PhysicsScene.PE.SetInterpolationAngularVelocity(PhysBody, _rotationalVelocity);
                ActivateIfPhysical(false);
            }
        }
    }
    public override bool Kinematic {
        get { return _kinematic; }
        set { _kinematic = value;
            // m_log.DebugFormat("{0}: Kinematic={1}", LogHeader, _kinematic);
        }
    }
    public override float Buoyancy {
        get { return _buoyancy; }
        set {
            _buoyancy = value;
            PhysicsScene.TaintedObject("BSPrim.setBuoyancy", delegate()
            {
                ForceBuoyancy = _buoyancy;
            });
        }
    }
    public override float ForceBuoyancy {
        get { return _buoyancy; }
        set {
            _buoyancy = value;
            // DetailLog("{0},BSPrim.setForceBuoyancy,taint,buoy={1}", LocalID, _buoyancy);
            // Force the recalculation of the various inertia,etc variables in the object
            UpdatePhysicalMassProperties(RawMass, true);
            DetailLog("{0},BSPrim.ForceBuoyancy,buoy={1},mass={2},grav={3}", LocalID, _buoyancy, RawMass, Gravity);
            ActivateIfPhysical(false);
        }
    }

    // Used for MoveTo
    public override OMV.Vector3 PIDTarget {
        set
        {
            // TODO: add a sanity check -- don't move more than a region or something like that.
            _PIDTarget = value;
        }
    }
    public override float PIDTau {
        set { _PIDTau = value; }
    }
    public override bool PIDActive {
        set {
            if (value)
            {
                // We're taking over after this.
                ZeroMotion(true);

                _targetMotor = new BSVMotor("BSPrim.PIDTarget",
                                            _PIDTau,                    // timeScale
                                            BSMotor.Infinite,           // decay time scale
                                            BSMotor.InfiniteVector,     // friction timescale
                                            1f                          // efficiency
                );
                _targetMotor.PhysicsScene = PhysicsScene; // DEBUG DEBUG so motor will output detail log messages.
                _targetMotor.SetTarget(_PIDTarget);
                _targetMotor.SetCurrent(RawPosition);
                /*
                _targetMotor = new BSPIDVMotor("BSPrim.PIDTarget");
                _targetMotor.PhysicsScene = PhysicsScene; // DEBUG DEBUG so motor will output detail log messages.

                _targetMotor.SetTarget(_PIDTarget);
                _targetMotor.SetCurrent(RawPosition);
                _targetMotor.TimeScale = _PIDTau;
                _targetMotor.Efficiency = 1f;
                 */

                RegisterPreStepAction("BSPrim.PIDTarget", LocalID, delegate(float timeStep)
                {
                    if (!IsPhysicallyActive)
                    {
                        UnRegisterPreStepAction("BSPrim.PIDTarget", LocalID);
                        return;
                    }

                    OMV.Vector3 origPosition = RawPosition;     // DEBUG DEBUG (for printout below)

                    // 'movePosition' is where we'd like the prim to be at this moment.
                    OMV.Vector3 movePosition = RawPosition + _targetMotor.Step(timeStep);

                    // If we are very close to our target, turn off the movement motor.
                    if (_targetMotor.ErrorIsZero())
                    {
                        DetailLog("{0},BSPrim.PIDTarget,zeroMovement,movePos={1},pos={2},mass={3}",
                                                LocalID, movePosition, RawPosition, Mass);
                        ForcePosition = _targetMotor.TargetValue;
                        _targetMotor.Enabled = false;
                    }
                    else
                    {
                        _position = movePosition;
                        PositionSanityCheck(true /* intaintTime */);
                        ForcePosition = _position;
                    }
                    DetailLog("{0},BSPrim.PIDTarget,move,fromPos={1},movePos={2}", LocalID, origPosition, movePosition);
                });
            }
            else
            {
                // Stop any targetting
                UnRegisterPreStepAction("BSPrim.PIDTarget", LocalID);
            }
        }
    }

    // Used for llSetHoverHeight and maybe vehicle height
    // Hover Height will override MoveTo target's Z
    public override bool PIDHoverActive {
        set {
            if (value)
            {
                // Turning the target on
                _hoverMotor = new BSFMotor("BSPrim.Hover",
                                            _PIDHoverTau,               // timeScale
                                            BSMotor.Infinite,           // decay time scale
                                            BSMotor.Infinite,           // friction timescale
                                            1f                          // efficiency
                );
                _hoverMotor.SetTarget(ComputeCurrentPIDHoverHeight());
                _hoverMotor.SetCurrent(RawPosition.Z);
                _hoverMotor.PhysicsScene = PhysicsScene; // DEBUG DEBUG so motor will output detail log messages.

                RegisterPreStepAction("BSPrim.Hover", LocalID, delegate(float timeStep)
                {
                    // Don't do hovering while the object is selected.
                    if (!IsPhysicallyActive)
                        return;

                    _hoverMotor.SetCurrent(RawPosition.Z);
                    _hoverMotor.SetTarget(ComputeCurrentPIDHoverHeight());
                    float targetHeight = _hoverMotor.Step(timeStep);

                    // 'targetHeight' is where we'd like the Z of the prim to be at this moment.
                    // Compute the amount of force to push us there.
                    float moveForce = (targetHeight - RawPosition.Z) * Mass;
                    // Undo anything the object thinks it's doing at the moment
                    moveForce = -RawVelocity.Z * Mass;

                    PhysicsScene.PE.ApplyCentralImpulse(PhysBody, new OMV.Vector3(0f, 0f, moveForce));
                    DetailLog("{0},BSPrim.Hover,move,targHt={1},moveForce={2},mass={3}", LocalID, targetHeight, moveForce, Mass);
                });
            }
            else
            {
                UnRegisterPreStepAction("BSPrim.Hover", LocalID);
            }
        }
    }
    public override float PIDHoverHeight {
        set { _PIDHoverHeight = value; }
    }
    public override PIDHoverType PIDHoverType {
        set { _PIDHoverType = value; }
    }
    public override float PIDHoverTau {
        set { _PIDHoverTau = value; }
    }
    // Based on current position, determine what we should be hovering at now.
    // Must recompute often. What if we walked offa cliff>
    private float ComputeCurrentPIDHoverHeight()
    {
        float ret = _PIDHoverHeight;
        float groundHeight = PhysicsScene.TerrainManager.GetTerrainHeightAtXYZ(RawPosition);

        switch (_PIDHoverType)
        {
            case PIDHoverType.Ground:
                ret = groundHeight + _PIDHoverHeight;
                break;
            case PIDHoverType.GroundAndWater:
                float waterHeight = PhysicsScene.TerrainManager.GetWaterLevelAtXYZ(RawPosition);
                if (groundHeight > waterHeight)
                {
                    ret = groundHeight + _PIDHoverHeight;
                }
                else
                {
                    ret = waterHeight + _PIDHoverHeight;
                }
                break;
        }
        return ret;
    }


    // For RotLookAt
    public override OMV.Quaternion APIDTarget { set { return; } }
    public override bool APIDActive { set { return; } }
    public override float APIDStrength { set { return; } }
    public override float APIDDamping { set { return; } }

    public override void AddForce(OMV.Vector3 force, bool pushforce) {
        // Per documentation, max force is limited.
        OMV.Vector3 addForce = Util.ClampV(force, BSParam.MaxAddForceMagnitude);

        // Since this force is being applied in only one step, make this a force per second.
        addForce /= PhysicsScene.LastTimeStep;
        AddForce(addForce, pushforce, false /* inTaintTime */);
    }

    // Applying a force just adds this to the total force on the object.
    // This added force will only last the next simulation tick.
    public void AddForce(OMV.Vector3 force, bool pushforce, bool inTaintTime) {
        // for an object, doesn't matter if force is a pushforce or not
        if (IsPhysicallyActive)
        {
            if (force.IsFinite())
            {
                // DetailLog("{0},BSPrim.addForce,call,force={1}", LocalID, addForce);

                OMV.Vector3 addForce = force;
                PhysicsScene.TaintedObject(inTaintTime, "BSPrim.AddForce", delegate()
                {
                    // Bullet adds this central force to the total force for this tick
                    DetailLog("{0},BSPrim.addForce,taint,force={1}", LocalID, addForce);
                    if (PhysBody.HasPhysicalBody)
                    {
                        PhysicsScene.PE.ApplyCentralForce(PhysBody, addForce);
                        ActivateIfPhysical(false);
                    }
                });
            }
            else
            {
                m_log.WarnFormat("{0}: AddForce: Got a NaN force applied to a prim. LocalID={1}", LogHeader, LocalID);
                return;
            }
        }
    }

    public void AddForceImpulse(OMV.Vector3 impulse, bool pushforce, bool inTaintTime) {
        // for an object, doesn't matter if force is a pushforce or not
        if (!IsPhysicallyActive)
        {
            if (impulse.IsFinite())
            {
                OMV.Vector3 addImpulse = Util.ClampV(impulse, BSParam.MaxAddForceMagnitude);
                // DetailLog("{0},BSPrim.addForceImpulse,call,impulse={1}", LocalID, impulse);

                PhysicsScene.TaintedObject(inTaintTime, "BSPrim.AddImpulse", delegate()
                {
                    // Bullet adds this impulse immediately to the velocity
                    DetailLog("{0},BSPrim.addForceImpulse,taint,impulseforce={1}", LocalID, addImpulse);
                    if (PhysBody.HasPhysicalBody)
                    {
                        PhysicsScene.PE.ApplyCentralImpulse(PhysBody, addImpulse);
                        ActivateIfPhysical(false);
                    }
                });
            }
            else
            {
                m_log.WarnFormat("{0}: AddForceImpulse: Got a NaN impulse applied to a prim. LocalID={1}", LogHeader, LocalID);
                return;
            }
        }
    }

    public override void AddAngularForce(OMV.Vector3 force, bool pushforce) {
        AddAngularForce(force, pushforce, false);
    }
    public void AddAngularForce(OMV.Vector3 force, bool pushforce, bool inTaintTime)
    {
        if (force.IsFinite())
        {
            OMV.Vector3 angForce = force;
            PhysicsScene.TaintedObject(inTaintTime, "BSPrim.AddAngularForce", delegate()
            {
                if (PhysBody.HasPhysicalBody)
                {
                    DetailLog("{0},BSPrim.AddAngularForce,taint,angForce={1}", LocalID, angForce);
                    PhysicsScene.PE.ApplyTorque(PhysBody, angForce);
                    ActivateIfPhysical(false);
                }
            });
        }
        else
        {
            m_log.WarnFormat("{0}: Got a NaN force applied to a prim. LocalID={1}", LogHeader, LocalID);
            return;
        }
    }

    // A torque impulse.
    // ApplyTorqueImpulse adds torque directly to the angularVelocity.
    // AddAngularForce accumulates the force and applied it to the angular velocity all at once.
    // Computed as: angularVelocity += impulse * inertia;
    public void ApplyTorqueImpulse(OMV.Vector3 impulse, bool inTaintTime)
    {
        OMV.Vector3 applyImpulse = impulse;
        PhysicsScene.TaintedObject(inTaintTime, "BSPrim.ApplyTorqueImpulse", delegate()
        {
            if (PhysBody.HasPhysicalBody)
            {
                PhysicsScene.PE.ApplyTorqueImpulse(PhysBody, applyImpulse);
                ActivateIfPhysical(false);
            }
        });
    }

    public override void SetMomentum(OMV.Vector3 momentum) {
        // DetailLog("{0},BSPrim.SetMomentum,call,mom={1}", LocalID, momentum);
    }
    #region Mass Calculation

    private float CalculateMass()
    {
        float volume = _size.X * _size.Y * _size.Z; // default
        float tmp;

        float returnMass = 0;
        float hollowAmount = (float)BaseShape.ProfileHollow * 2.0e-5f;
        float hollowVolume = hollowAmount * hollowAmount;

        switch (BaseShape.ProfileShape)
        {
            case ProfileShape.Square:
                // default box

                if (BaseShape.PathCurve == (byte)Extrusion.Straight)
                    {
                    if (hollowAmount > 0.0)
                        {
                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Square:
                            case HollowShape.Same:
                                break;

                            case HollowShape.Circle:

                                hollowVolume *= 0.78539816339f;
                                break;

                            case HollowShape.Triangle:

                                hollowVolume *= (0.5f * .5f);
                                break;

                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }

                else if (BaseShape.PathCurve == (byte)Extrusion.Curve1)
                    {
                    //a tube

                    volume *= 0.78539816339e-2f * (float)(200 - BaseShape.PathScaleX);
                    tmp= 1.0f -2.0e-2f * (float)(200 - BaseShape.PathScaleY);
                    volume -= volume*tmp*tmp;

                    if (hollowAmount > 0.0)
                        {
                        hollowVolume *= hollowAmount;

                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Square:
                            case HollowShape.Same:
                                break;

                            case HollowShape.Circle:
                                hollowVolume *= 0.78539816339f;;
                                break;

                            case HollowShape.Triangle:
                                hollowVolume *= 0.5f * 0.5f;
                                break;
                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }

                break;

            case ProfileShape.Circle:

                if (BaseShape.PathCurve == (byte)Extrusion.Straight)
                    {
                    volume *= 0.78539816339f; // elipse base

                    if (hollowAmount > 0.0)
                        {
                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Same:
                            case HollowShape.Circle:
                                break;

                            case HollowShape.Square:
                                hollowVolume *= 0.5f * 2.5984480504799f;
                                break;

                            case HollowShape.Triangle:
                                hollowVolume *= .5f * 1.27323954473516f;
                                break;

                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }

                else if (BaseShape.PathCurve == (byte)Extrusion.Curve1)
                    {
                    volume *= 0.61685027506808491367715568749226e-2f * (float)(200 - BaseShape.PathScaleX);
                    tmp = 1.0f - .02f * (float)(200 - BaseShape.PathScaleY);
                    volume *= (1.0f - tmp * tmp);

                    if (hollowAmount > 0.0)
                        {

                        // calculate the hollow volume by it's shape compared to the prim shape
                        hollowVolume *= hollowAmount;

                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Same:
                            case HollowShape.Circle:
                                break;

                            case HollowShape.Square:
                                hollowVolume *= 0.5f * 2.5984480504799f;
                                break;

                            case HollowShape.Triangle:
                                hollowVolume *= .5f * 1.27323954473516f;
                                break;

                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }
                break;

            case ProfileShape.HalfCircle:
                if (BaseShape.PathCurve == (byte)Extrusion.Curve1)
                {
                volume *= 0.52359877559829887307710723054658f;
                }
                break;

            case ProfileShape.EquilateralTriangle:

                if (BaseShape.PathCurve == (byte)Extrusion.Straight)
                    {
                    volume *= 0.32475953f;

                    if (hollowAmount > 0.0)
                        {

                        // calculate the hollow volume by it's shape compared to the prim shape
                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Same:
                            case HollowShape.Triangle:
                                hollowVolume *= .25f;
                                break;

                            case HollowShape.Square:
                                hollowVolume *= 0.499849f * 3.07920140172638f;
                                break;

                            case HollowShape.Circle:
                                // Hollow shape is a perfect cyllinder in respect to the cube's scale
                                // Cyllinder hollow volume calculation

                                hollowVolume *= 0.1963495f * 3.07920140172638f;
                                break;

                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }
                else if (BaseShape.PathCurve == (byte)Extrusion.Curve1)
                    {
                    volume *= 0.32475953f;
                    volume *= 0.01f * (float)(200 - BaseShape.PathScaleX);
                    tmp = 1.0f - .02f * (float)(200 - BaseShape.PathScaleY);
                    volume *= (1.0f - tmp * tmp);

                    if (hollowAmount > 0.0)
                        {

                        hollowVolume *= hollowAmount;

                        switch (BaseShape.HollowShape)
                            {
                            case HollowShape.Same:
                            case HollowShape.Triangle:
                                hollowVolume *= .25f;
                                break;

                            case HollowShape.Square:
                                hollowVolume *= 0.499849f * 3.07920140172638f;
                                break;

                            case HollowShape.Circle:

                                hollowVolume *= 0.1963495f * 3.07920140172638f;
                                break;

                            default:
                                hollowVolume = 0;
                                break;
                            }
                        volume *= (1.0f - hollowVolume);
                        }
                    }
                    break;

            default:
                break;
            }



        float taperX1;
        float taperY1;
        float taperX;
        float taperY;
        float pathBegin;
        float pathEnd;
        float profileBegin;
        float profileEnd;

        if (BaseShape.PathCurve == (byte)Extrusion.Straight || BaseShape.PathCurve == (byte)Extrusion.Flexible)
            {
            taperX1 = BaseShape.PathScaleX * 0.01f;
            if (taperX1 > 1.0f)
                taperX1 = 2.0f - taperX1;
            taperX = 1.0f - taperX1;

            taperY1 = BaseShape.PathScaleY * 0.01f;
            if (taperY1 > 1.0f)
                taperY1 = 2.0f - taperY1;
            taperY = 1.0f - taperY1;
            }
        else
            {
            taperX = BaseShape.PathTaperX * 0.01f;
            if (taperX < 0.0f)
                taperX = -taperX;
            taperX1 = 1.0f - taperX;

            taperY = BaseShape.PathTaperY * 0.01f;
            if (taperY < 0.0f)
                taperY = -taperY;
            taperY1 = 1.0f - taperY;

            }


        volume *= (taperX1 * taperY1 + 0.5f * (taperX1 * taperY + taperX * taperY1) + 0.3333333333f * taperX * taperY);

        pathBegin = (float)BaseShape.PathBegin * 2.0e-5f;
        pathEnd = 1.0f - (float)BaseShape.PathEnd * 2.0e-5f;
        volume *= (pathEnd - pathBegin);

        // this is crude aproximation
        profileBegin = (float)BaseShape.ProfileBegin * 2.0e-5f;
        profileEnd = 1.0f - (float)BaseShape.ProfileEnd * 2.0e-5f;
        volume *= (profileEnd - profileBegin);

        returnMass = Density * BSParam.DensityScaleFactor * volume;
        DetailLog("{0},BSPrim.CalculateMass,den={1},vol={2},mass={3}", LocalID, Density, volume, returnMass);

        returnMass = Util.Clamp(returnMass, BSParam.MinimumObjectMass, BSParam.MaximumObjectMass);

        return returnMass;
    }// end CalculateMass
    #endregion Mass Calculation

    // Rebuild the geometry and object.
    // This is called when the shape changes so we need to recreate the mesh/hull.
    // Called at taint-time!!!
    public void CreateGeomAndObject(bool forceRebuild)
    {
        // Create the correct physical representation for this type of object.
        // Updates base.PhysBody and base.PhysShape with the new information.
        // Ignore 'forceRebuild'. This routine makes the right choices and changes of necessary.
        PhysicsScene.Shapes.GetBodyAndShape(false /*forceRebuild */, PhysicsScene.World, this, null, delegate(BulletBody dBody)
        {
            // Called if the current prim body is about to be destroyed.
            // Remove all the physical dependencies on the old body.
            // (Maybe someday make the changing of BSShape an event to be subscribed to by BSLinkset, ...)
            RemoveBodyDependencies();
        });

        // Make sure the properties are set on the new object
        UpdatePhysicalParameters();
        return;
    }

    protected virtual void RemoveBodyDependencies()
    {
        VehicleController.RemoveBodyDependencies(this);
    }

    // The physics engine says that properties have updated. Update same and inform
    // the world that things have changed.
    public override void UpdateProperties(EntityProperties entprop)
    {
        TriggerPreUpdatePropertyAction(ref entprop);

        // A temporary kludge to suppress the rotational effects introduced on vehicles by Bullet
        // TODO: handle physics introduced by Bullet with computed vehicle physics.
        if (VehicleController.IsActive)
        {
            entprop.RotationalVelocity = OMV.Vector3.Zero;
        }

        // DetailLog("{0},BSPrim.UpdateProperties,entry,entprop={1}", LocalID, entprop);   // DEBUG DEBUG

        // Assign directly to the local variables so the normal set actions do not happen
        _position = entprop.Position;
        _orientation = entprop.Rotation;
        // DEBUG DEBUG DEBUG -- smooth velocity changes a bit. The simulator seems to be
        //    very sensitive to velocity changes.
        if (entprop.Velocity == OMV.Vector3.Zero || !entprop.Velocity.ApproxEquals(_velocity, BSParam.UpdateVelocityChangeThreshold))
            _velocity = entprop.Velocity;
        _acceleration = entprop.Acceleration;
        _rotationalVelocity = entprop.RotationalVelocity;

        // DetailLog("{0},BSPrim.UpdateProperties,afterAssign,entprop={1}", LocalID, entprop);   // DEBUG DEBUG

        // The sanity check can change the velocity and/or position.
        if (PositionSanityCheck(true /* inTaintTime */ ))
        {
            entprop.Position = _position;
            entprop.Velocity = _velocity;
            entprop.RotationalVelocity = _rotationalVelocity;
            entprop.Acceleration = _acceleration;
        }

        OMV.Vector3 direction = OMV.Vector3.UnitX * _orientation;   // DEBUG DEBUG DEBUG
        DetailLog("{0},BSPrim.UpdateProperties,call,entProp={1},dir={2}", LocalID, entprop, direction);

        // remember the current and last set values
        LastEntityProperties = CurrentEntityProperties;
        CurrentEntityProperties = entprop;

        base.RequestPhysicsterseUpdate();
            /*
        else
        {
            // For debugging, report the movement of children
            DetailLog("{0},BSPrim.UpdateProperties,child,pos={1},orient={2},vel={3},accel={4},rotVel={5}",
                    LocalID, entprop.Position, entprop.Rotation, entprop.Velocity,
                    entprop.Acceleration, entprop.RotationalVelocity);
        }
             */
    }
}
}
