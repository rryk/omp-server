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
using System.Collections.Generic;
using System.Reflection;
using log4net;
using OMV = OpenMetaverse;
using OpenSim.Framework;
using OpenSim.Region.Physics.Manager;

namespace OpenSim.Region.Physics.BulletSPlugin
{
public sealed class BSCharacter : BSPhysObject
{
    private static readonly ILog m_log = LogManager.GetLogger(MethodBase.GetCurrentMethod().DeclaringType);
    private static readonly string LogHeader = "[BULLETS CHAR]";

    // private bool _stopped;
    private OMV.Vector3 _size;
    private bool _grabbed;
    private bool _selected;
    private OMV.Vector3 _position;
    private float _mass;
    private float _avatarVolume;
    private OMV.Vector3 _force;
    private OMV.Vector3 _velocity;
    private OMV.Vector3 _torque;
    private float _collisionScore;
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

    private BSVMotor _velocityMotor;

    private OMV.Vector3 _PIDTarget;
    private bool _usePID;
    private float _PIDTau;
    private bool _useHoverPID;
    private float _PIDHoverHeight;
    private PIDHoverType _PIDHoverType;
    private float _PIDHoverTao;

    public BSCharacter(uint localID, String avName, BSScene parent_scene, OMV.Vector3 pos, OMV.Vector3 size, bool isFlying)
            : base(parent_scene, localID, avName, "BSCharacter")
    {
        _physicsActorType = (int)ActorTypes.Agent;
        _position = pos;

        _flying = isFlying;
        _orientation = OMV.Quaternion.Identity;
        _velocity = OMV.Vector3.Zero;
        _buoyancy = ComputeBuoyancyFromFlying(isFlying);
        Friction = BSParam.AvatarStandingFriction;
        Density = BSParam.AvatarDensity / BSParam.DensityScaleFactor;

        // Old versions of ScenePresence passed only the height. If width and/or depth are zero,
        //     replace with the default values.
        _size = size;
        if (_size.X == 0f) _size.X = BSParam.AvatarCapsuleDepth;
        if (_size.Y == 0f) _size.Y = BSParam.AvatarCapsuleWidth;

        // The dimensions of the physical capsule are kept in the scale.
        // Physics creates a unit capsule which is scaled by the physics engine.
        Scale = ComputeAvatarScale(_size);
        // set _avatarVolume and _mass based on capsule size, _density and Scale
        ComputeAvatarVolumeAndMass();

        SetupMovementMotor();

        DetailLog("{0},BSCharacter.create,call,size={1},scale={2},density={3},volume={4},mass={5}",
                            LocalID, _size, Scale, Density, _avatarVolume, RawMass);

        // do actual creation in taint time
        PhysicsScene.TaintedObject("BSCharacter.create", delegate()
        {
            DetailLog("{0},BSCharacter.create,taint", LocalID);
            // New body and shape into PhysBody and PhysShape
            PhysicsScene.Shapes.GetBodyAndShape(true, PhysicsScene.World, this);

            SetPhysicalProperties();
        });
        return;
    }

    // called when this character is being destroyed and the resources should be released
    public override void Destroy()
    {
        base.Destroy();

        DetailLog("{0},BSCharacter.Destroy", LocalID);
        PhysicsScene.TaintedObject("BSCharacter.destroy", delegate()
        {
            PhysicsScene.Shapes.DereferenceBody(PhysBody, null /* bodyCallback */);
            PhysBody.Clear();
            PhysicsScene.Shapes.DereferenceShape(PhysShape, null /* bodyCallback */);
            PhysShape.Clear();
        });
    }

    private void SetPhysicalProperties()
    {
        PhysicsScene.PE.RemoveObjectFromWorld(PhysicsScene.World, PhysBody);

        ZeroMotion(true);
        ForcePosition = _position;

        // Set the velocity
        _velocityMotor.Reset();
        _velocityMotor.SetTarget(_velocity);
        _velocityMotor.SetCurrent(_velocity);
        ForceVelocity = _velocity;

        // This will enable or disable the flying buoyancy of the avatar.
        // Needs to be reset especially when an avatar is recreated after crossing a region boundry.
        Flying = _flying;

        PhysicsScene.PE.SetRestitution(PhysBody, BSParam.AvatarRestitution);
        PhysicsScene.PE.SetMargin(PhysShape, PhysicsScene.Params.collisionMargin);
        PhysicsScene.PE.SetLocalScaling(PhysShape, Scale);
        PhysicsScene.PE.SetContactProcessingThreshold(PhysBody, BSParam.ContactProcessingThreshold);
        if (BSParam.CcdMotionThreshold > 0f)
        {
            PhysicsScene.PE.SetCcdMotionThreshold(PhysBody, BSParam.CcdMotionThreshold);
            PhysicsScene.PE.SetCcdSweptSphereRadius(PhysBody, BSParam.CcdSweptSphereRadius);
        }

        UpdatePhysicalMassProperties(RawMass, false);

        // Make so capsule does not fall over
        PhysicsScene.PE.SetAngularFactorV(PhysBody, OMV.Vector3.Zero);

        PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.CF_CHARACTER_OBJECT);

        PhysicsScene.PE.AddObjectToWorld(PhysicsScene.World, PhysBody);

        // PhysicsScene.PE.ForceActivationState(PhysBody, ActivationState.ACTIVE_TAG);
        PhysicsScene.PE.ForceActivationState(PhysBody, ActivationState.DISABLE_DEACTIVATION);
        PhysicsScene.PE.UpdateSingleAabb(PhysicsScene.World, PhysBody);

        // Do this after the object has been added to the world
        PhysBody.collisionType = CollisionType.Avatar;
        PhysBody.ApplyCollisionMask(PhysicsScene);
    }

    // The avatar's movement is controlled by this motor that speeds up and slows down
    //    the avatar seeking to reach the motor's target speed.
    // This motor runs as a prestep action for the avatar so it will keep the avatar
    //    standing as well as moving. Destruction of the avatar will destroy the pre-step action.
    private void SetupMovementMotor()
    {
        // Infinite decay and timescale values so motor only changes current to target values.
        _velocityMotor = new BSVMotor("BSCharacter.Velocity", 
                                            0.2f,                       // time scale
                                            BSMotor.Infinite,           // decay time scale
                                            BSMotor.InfiniteVector,     // friction timescale
                                            1f                          // efficiency
        );
        // _velocityMotor.PhysicsScene = PhysicsScene; // DEBUG DEBUG so motor will output detail log messages.

        RegisterPreStepAction("BSCharactor.Movement", LocalID, delegate(float timeStep)
        {
            // TODO: Decide if the step parameters should be changed depending on the avatar's
            //     state (flying, colliding, ...). There is code in ODE to do this.

            // COMMENTARY: when the user is making the avatar walk, except for falling, the velocity
            //   specified for the avatar is the one that should be used. For falling, if the avatar
            //   is not flying and is not colliding then it is presumed to be falling and the Z
            //   component is not fooled with (thus allowing gravity to do its thing).
            // When the avatar is standing, though, the user has specified a velocity of zero and
            //   the avatar should be standing. But if the avatar is pushed by something in the world
            //   (raising elevator platform, moving vehicle, ...) the avatar should be allowed to
            //   move. Thus, the velocity cannot be forced to zero. The problem is that small velocity
            //   errors can creap in and the avatar will slowly float off in some direction.
            // So, the problem is that, when an avatar is standing, we cannot tell creaping error
            //   from real pushing.
            // The code below uses whether the collider is static or moving to decide whether to zero motion.

            _velocityMotor.Step(timeStep);

            // If we're not supposed to be moving, make sure things are zero.
            if (_velocityMotor.ErrorIsZero() && _velocityMotor.TargetValue == OMV.Vector3.Zero)
            {
                // The avatar shouldn't be moving
                _velocityMotor.Zero();

                if (IsColliding)
                {
                    // If we are colliding with a stationary object, presume we're standing and don't move around
                    if (!ColliderIsMoving)
                    {
                        DetailLog("{0},BSCharacter.MoveMotor,collidingWithStationary,zeroingMotion", LocalID);
                        ZeroMotion(true /* inTaintTime */);
                    }

                    // Standing has more friction on the ground
                    if (Friction != BSParam.AvatarStandingFriction)
                    {
                        Friction = BSParam.AvatarStandingFriction;
                        PhysicsScene.PE.SetFriction(PhysBody, Friction);
                    }
                }
                else
                {
                    if (Flying)
                    {
                        // Flying and not collising and velocity nearly zero.
                        ZeroMotion(true /* inTaintTime */);
                    }
                }

                DetailLog("{0},BSCharacter.MoveMotor,taint,stopping,target={1},colliding={2}", LocalID, _velocityMotor.TargetValue, IsColliding);
            }
            else
            {
                // Supposed to be moving.
                OMV.Vector3 stepVelocity = _velocityMotor.CurrentValue;

                if (Friction != BSParam.AvatarFriction)
                {
                    // Probably starting up walking. Set friction to moving friction.
                    Friction = BSParam.AvatarFriction;
                    PhysicsScene.PE.SetFriction(PhysBody, Friction);
                }

                // If falling, we keep the world's downward vector no matter what the other axis specify.
                // The check for _velocity.Z < 0 makes jumping work (temporary upward force).
                if (!Flying && !IsColliding)
                {
                    if (_velocity.Z < 0)
                        stepVelocity.Z = _velocity.Z;
                    // DetailLog("{0},BSCharacter.MoveMotor,taint,overrideStepZWithWorldZ,stepVel={1}", LocalID, stepVelocity);
                }

                // 'stepVelocity' is now the speed we'd like the avatar to move in. Turn that into an instantanous force.
                OMV.Vector3 moveForce = (stepVelocity - _velocity) * Mass;

                // Should we check for move force being small and forcing velocity to zero?

                // Add special movement force to allow avatars to walk up stepped surfaces.
                moveForce += WalkUpStairs();

                DetailLog("{0},BSCharacter.MoveMotor,move,stepVel={1},vel={2},mass={3},moveForce={4}", LocalID, stepVelocity, _velocity, Mass, moveForce);
                PhysicsScene.PE.ApplyCentralImpulse(PhysBody, moveForce);
            }
        });
    }

    // Decide if the character is colliding with a low object and compute a force to pop the
    //    avatar up so it can walk up and over the low objects.
    private OMV.Vector3 WalkUpStairs()
    {
        OMV.Vector3 ret = OMV.Vector3.Zero;

        // This test is done if moving forward, not flying and is colliding with something.
        // DetailLog("{0},BSCharacter.WalkUpStairs,IsColliding={1},flying={2},targSpeed={3},collisions={4}",
        //                 LocalID, IsColliding, Flying, TargetSpeed, CollisionsLastTick.Count);
        if (IsColliding && !Flying && TargetVelocitySpeed > 0.1f /* && ForwardSpeed < 0.1f */)
        {
            // The range near the character's feet where we will consider stairs
            float nearFeetHeightMin = RawPosition.Z - (Size.Z / 2f) + 0.05f;
            float nearFeetHeightMax = nearFeetHeightMin + BSParam.AvatarStepHeight;

            // Look for a collision point that is near the character's feet and is oriented the same as the charactor is
            foreach (KeyValuePair<uint, ContactPoint> kvp in CollisionsLastTick.m_objCollisionList)
            {
                // Don't care about collisions with the terrain
                if (kvp.Key > PhysicsScene.TerrainManager.HighestTerrainID)
                {
                    OMV.Vector3 touchPosition = kvp.Value.Position;
                    // DetailLog("{0},BSCharacter.WalkUpStairs,min={1},max={2},touch={3}",
                    //                 LocalID, nearFeetHeightMin, nearFeetHeightMax, touchPosition);
                    if (touchPosition.Z >= nearFeetHeightMin && touchPosition.Z <= nearFeetHeightMax)
                    {
                        // This contact is within the 'near the feet' range.
                        // The normal should be our contact point to the object so it is pointing away
                        //    thus the difference between our facing orientation and the normal should be small.
                        OMV.Vector3 directionFacing = OMV.Vector3.UnitX * RawOrientation;
                        OMV.Vector3 touchNormal = OMV.Vector3.Normalize(kvp.Value.SurfaceNormal);
                        float diff = Math.Abs(OMV.Vector3.Distance(directionFacing, touchNormal));
                        if (diff < BSParam.AvatarStepApproachFactor)
                        {
                            // Found the stairs contact point. Push up a little to raise the character.
                            float upForce = (touchPosition.Z - nearFeetHeightMin) * Mass * BSParam.AvatarStepForceFactor;
                            ret = new OMV.Vector3(0f, 0f, upForce);

                            // Also move the avatar up for the new height
                            OMV.Vector3 displacement = new OMV.Vector3(0f, 0f, BSParam.AvatarStepHeight / 2f);
                            ForcePosition = RawPosition + displacement;
                        }
                        DetailLog("{0},BSCharacter.WalkUpStairs,touchPos={1},nearFeetMin={2},faceDir={3},norm={4},diff={5},ret={6}",
                                LocalID, touchPosition, nearFeetHeightMin, directionFacing, touchNormal, diff, ret);
                    }
                }
            }
        }

        return ret;
    }

    public override void RequestPhysicsterseUpdate()
    {
        base.RequestPhysicsterseUpdate();
    }
    // No one calls this method so I don't know what it could possibly mean
    public override bool Stopped { get { return false; } }

    public override OMV.Vector3 Size {
        get
        {
            // Avatar capsule size is kept in the scale parameter.
            return _size;
        }

        set {
            _size = value;
            // Old versions of ScenePresence passed only the height. If width and/or depth are zero,
            //     replace with the default values.
            if (_size.X == 0f) _size.X = BSParam.AvatarCapsuleDepth;
            if (_size.Y == 0f) _size.Y = BSParam.AvatarCapsuleWidth;

            Scale = ComputeAvatarScale(_size);
            ComputeAvatarVolumeAndMass();
            DetailLog("{0},BSCharacter.setSize,call,size={1},scale={2},density={3},volume={4},mass={5}",
                            LocalID, _size, Scale, Density, _avatarVolume, RawMass);

            PhysicsScene.TaintedObject("BSCharacter.setSize", delegate()
            {
                if (PhysBody.HasPhysicalBody && PhysShape.HasPhysicalShape)
                {
                    PhysicsScene.PE.SetLocalScaling(PhysShape, Scale);
                    UpdatePhysicalMassProperties(RawMass, true);
                    // Make sure this change appears as a property update event
                    PhysicsScene.PE.PushUpdate(PhysBody);
                }
            });

        }
    }

    public override PrimitiveBaseShape Shape
    {
        set { BaseShape = value; }
    }
    // I want the physics engine to make an avatar capsule
    public override BSPhysicsShapeType PreferredPhysicalShape
    {
        get {return BSPhysicsShapeType.SHAPE_CAPSULE; }
    }

    public override bool Grabbed {
        set { _grabbed = value; }
    }
    public override bool Selected {
        set { _selected = value; }
    }
    public override bool IsSelected
    {
        get { return _selected; }
    }
    public override void CrossingFailure() { return; }
    public override void link(PhysicsActor obj) { return; }
    public override void delink() { return; }

    // Set motion values to zero.
    // Do it to the properties so the values get set in the physics engine.
    // Push the setting of the values to the viewer.
    // Called at taint time!
    public override void ZeroMotion(bool inTaintTime)
    {
        _velocity = OMV.Vector3.Zero;
        _acceleration = OMV.Vector3.Zero;
        _rotationalVelocity = OMV.Vector3.Zero;

        // Zero some other properties directly into the physics engine
        PhysicsScene.TaintedObject(inTaintTime, "BSCharacter.ZeroMotion", delegate()
        {
            if (PhysBody.HasPhysicalBody)
                PhysicsScene.PE.ClearAllForces(PhysBody);
        });
    }
    public override void ZeroAngularMotion(bool inTaintTime)
    {
        _rotationalVelocity = OMV.Vector3.Zero;

        PhysicsScene.TaintedObject(inTaintTime, "BSCharacter.ZeroMotion", delegate()
        {
            if (PhysBody.HasPhysicalBody)
            {
                PhysicsScene.PE.SetInterpolationAngularVelocity(PhysBody, OMV.Vector3.Zero);
                PhysicsScene.PE.SetAngularVelocity(PhysBody, OMV.Vector3.Zero);
                // The next also get rid of applied linear force but the linear velocity is untouched.
                PhysicsScene.PE.ClearForces(PhysBody);
            }
        });
    }


    public override void LockAngularMotion(OMV.Vector3 axis) { return; }

    public override OMV.Vector3 RawPosition
    {
        get { return _position; }
        set { _position = value; }
    }
    public override OMV.Vector3 Position {
        get {
            // Don't refetch the position because this function is called a zillion times
            // _position = PhysicsScene.PE.GetObjectPosition(Scene.World, LocalID);
            return _position;
        }
        set {
            _position = value;

            PhysicsScene.TaintedObject("BSCharacter.setPosition", delegate()
            {
                DetailLog("{0},BSCharacter.SetPosition,taint,pos={1},orient={2}", LocalID, _position, _orientation);
                PositionSanityCheck();
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
            }
        }
    }


    // Check that the current position is sane and, if not, modify the position to make it so.
    // Check for being below terrain or on water.
    // Returns 'true' of the position was made sane by some action.
    private bool PositionSanityCheck()
    {
        bool ret = false;

        // TODO: check for out of bounds
        if (!PhysicsScene.TerrainManager.IsWithinKnownTerrain(RawPosition))
        {
            // The character is out of the known/simulated area.
            // Force the avatar position to be within known. ScenePresence will use the position
            //    plus the velocity to decide if the avatar is moving out of the region.
            RawPosition = PhysicsScene.TerrainManager.ClampPositionIntoKnownTerrain(RawPosition);
            DetailLog("{0},BSCharacter.PositionSanityCheck,notWithinKnownTerrain,clampedPos={1}", LocalID, RawPosition);
            return true;
        }

        // If below the ground, move the avatar up
        float terrainHeight = PhysicsScene.TerrainManager.GetTerrainHeightAtXYZ(RawPosition);
        if (Position.Z < terrainHeight)
        {
            DetailLog("{0},BSCharacter.PositionSanityCheck,adjustForUnderGround,pos={1},terrain={2}", LocalID, _position, terrainHeight);
            _position.Z = terrainHeight + BSParam.AvatarBelowGroundUpCorrectionMeters;
            ret = true;
        }
        if ((CurrentCollisionFlags & CollisionFlags.BS_FLOATS_ON_WATER) != 0)
        {
            float waterHeight = PhysicsScene.TerrainManager.GetWaterLevelAtXYZ(_position);
            if (Position.Z < waterHeight)
            {
                _position.Z = waterHeight;
                ret = true;
            }
        }

        return ret;
    }

    // A version of the sanity check that also makes sure a new position value is
    //    pushed back to the physics engine. This routine would be used by anyone
    //    who is not already pushing the value.
    private bool PositionSanityCheck(bool inTaintTime)
    {
        bool ret = false;
        if (PositionSanityCheck())
        {
            // The new position value must be pushed into the physics engine but we can't
            //    just assign to "Position" because of potential call loops.
            PhysicsScene.TaintedObject(inTaintTime, "BSCharacter.PositionSanityCheck", delegate()
            {
                DetailLog("{0},BSCharacter.PositionSanityCheck,taint,pos={1},orient={2}", LocalID, _position, _orientation);
                ForcePosition = _position;
            });
            ret = true;
        }
        return ret;
    }

    public override float Mass { get { return _mass; } }

    // used when we only want this prim's mass and not the linkset thing
    public override float RawMass { 
        get {return _mass; }
    }
    public override void UpdatePhysicalMassProperties(float physMass, bool inWorld)
    {
        OMV.Vector3 localInertia = PhysicsScene.PE.CalculateLocalInertia(PhysShape, physMass);
        PhysicsScene.PE.SetMassProps(PhysBody, physMass, localInertia);
    }

    public override OMV.Vector3 Force {
        get { return _force; }
        set {
            _force = value;
            // m_log.DebugFormat("{0}: Force = {1}", LogHeader, _force);
            PhysicsScene.TaintedObject("BSCharacter.SetForce", delegate()
            {
                DetailLog("{0},BSCharacter.setForce,taint,force={1}", LocalID, _force);
                if (PhysBody.HasPhysicalBody)
                    PhysicsScene.PE.SetObjectForce(PhysBody, _force);
            });
        }
    }

    // Avatars don't do vehicles
    public override int VehicleType { get { return (int)Vehicle.TYPE_NONE; } set { return; } }
    public override void VehicleFloatParam(int param, float value) { }
    public override void VehicleVectorParam(int param, OMV.Vector3 value) {}
    public override void VehicleRotationParam(int param, OMV.Quaternion rotation) { }
    public override void VehicleFlags(int param, bool remove) { }

    // Allows the detection of collisions with inherently non-physical prims. see llVolumeDetect for more
    public override void SetVolumeDetect(int param) { return; }

    public override OMV.Vector3 GeometricCenter { get { return OMV.Vector3.Zero; } }
    public override OMV.Vector3 CenterOfMass { get { return OMV.Vector3.Zero; } }

    // Sets the target in the motor. This starts the changing of the avatar's velocity.
    public override OMV.Vector3 TargetVelocity
    {
        get
        {
            return m_targetVelocity;
        }
        set
        {
            DetailLog("{0},BSCharacter.setTargetVelocity,call,vel={1}", LocalID, value);
            m_targetVelocity = value;
            OMV.Vector3 targetVel = value;
            if (_setAlwaysRun)
                targetVel *= new OMV.Vector3(BSParam.AvatarAlwaysRunFactor, BSParam.AvatarAlwaysRunFactor, 0f);

            PhysicsScene.TaintedObject("BSCharacter.setTargetVelocity", delegate()
            {
                _velocityMotor.Reset();
                _velocityMotor.SetTarget(targetVel);
                _velocityMotor.SetCurrent(_velocity);
                _velocityMotor.Enabled = true;
            });
        }
    }
    public override OMV.Vector3 RawVelocity
    {
        get { return _velocity; }
        set { _velocity = value; }
    }
    // Directly setting velocity means this is what the user really wants now.
    public override OMV.Vector3 Velocity {
        get { return _velocity; }
        set {
            _velocity = value;
            // m_log.DebugFormat("{0}: set velocity = {1}", LogHeader, _velocity);
            PhysicsScene.TaintedObject("BSCharacter.setVelocity", delegate()
            {
                _velocityMotor.Reset();
                _velocityMotor.SetCurrent(_velocity);
                _velocityMotor.SetTarget(_velocity);
                _velocityMotor.Enabled = false;

                DetailLog("{0},BSCharacter.setVelocity,taint,vel={1}", LocalID, _velocity);
                ForceVelocity = _velocity;
            });
        }
    }
    public override OMV.Vector3 ForceVelocity {
        get { return _velocity; }
        set {
            PhysicsScene.AssertInTaintTime("BSCharacter.ForceVelocity");

            _velocity = value;
            PhysicsScene.PE.SetLinearVelocity(PhysBody, _velocity);
            PhysicsScene.PE.Activate(PhysBody, true);
        }
    }
    public override OMV.Vector3 Torque {
        get { return _torque; }
        set { _torque = value;
        }
    }
    public override float CollisionScore {
        get { return _collisionScore; }
        set { _collisionScore = value;
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
        get { return _orientation; }
        set {
            // Orientation is set zillions of times when an avatar is walking. It's like
            //      the viewer doesn't trust us.
            if (_orientation != value)
            {
                _orientation = value;
                PhysicsScene.TaintedObject("BSCharacter.setOrientation", delegate()
                {
                    ForceOrientation = _orientation;
                });
            }
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
            {
                // _position = PhysicsScene.PE.GetPosition(BSBody);
                PhysicsScene.PE.SetTranslation(PhysBody, _position, _orientation);
            }
        }
    }
    public override int PhysicsActorType {
        get { return _physicsActorType; }
        set { _physicsActorType = value;
        }
    }
    public override bool IsPhysical {
        get { return _isPhysical; }
        set { _isPhysical = value;
        }
    }
    public override bool IsSolid {
        get { return true; }
    }
    public override bool IsStatic {
        get { return false; }
    }
    public override bool IsPhysicallyActive {
        get { return true; }
    }
    public override bool Flying {
        get { return _flying; }
        set {
            _flying = value;

            // simulate flying by changing the effect of gravity
            Buoyancy = ComputeBuoyancyFromFlying(_flying);
        }
    }
    // Flying is implimented by changing the avatar's buoyancy.
    // Would this be done better with a vehicle type?
    private float ComputeBuoyancyFromFlying(bool ifFlying) {
        return ifFlying ? 1f : 0f;
    }
    public override bool
        SetAlwaysRun {
        get { return _setAlwaysRun; }
        set { _setAlwaysRun = value; }
    }
    public override bool ThrottleUpdates {
        get { return _throttleUpdates; }
        set { _throttleUpdates = value; }
    }
    public override bool FloatOnWater {
        set {
            _floatOnWater = value;
            PhysicsScene.TaintedObject("BSCharacter.setFloatOnWater", delegate()
            {
                if (PhysBody.HasPhysicalBody)
                {
                    if (_floatOnWater)
                        CurrentCollisionFlags = PhysicsScene.PE.AddToCollisionFlags(PhysBody, CollisionFlags.BS_FLOATS_ON_WATER);
                    else
                        CurrentCollisionFlags = PhysicsScene.PE.RemoveFromCollisionFlags(PhysBody, CollisionFlags.BS_FLOATS_ON_WATER);
                }
            });
        }
    }
    public override OMV.Vector3 RotationalVelocity {
        get { return _rotationalVelocity; }
        set { _rotationalVelocity = value; }
    }
    public override OMV.Vector3 ForceRotationalVelocity {
        get { return _rotationalVelocity; }
        set { _rotationalVelocity = value; }
    }
    public override bool Kinematic {
        get { return _kinematic; }
        set { _kinematic = value; }
    }
    // neg=fall quickly, 0=1g, 1=0g, pos=float up
    public override float Buoyancy {
        get { return _buoyancy; }
        set { _buoyancy = value;
            PhysicsScene.TaintedObject("BSCharacter.setBuoyancy", delegate()
            {
                DetailLog("{0},BSCharacter.setBuoyancy,taint,buoy={1}", LocalID, _buoyancy);
                ForceBuoyancy = _buoyancy;
            });
        }
    }
    public override float ForceBuoyancy {
        get { return _buoyancy; }
        set { 
            PhysicsScene.AssertInTaintTime("BSCharacter.ForceBuoyancy");

            _buoyancy = value;
            DetailLog("{0},BSCharacter.setForceBuoyancy,taint,buoy={1}", LocalID, _buoyancy);
            // Buoyancy is faked by changing the gravity applied to the object
            float  grav = BSParam.Gravity * (1f - _buoyancy);
            Gravity = new OMV.Vector3(0f, 0f, grav);
            if (PhysBody.HasPhysicalBody)
                PhysicsScene.PE.SetGravity(PhysBody, Gravity);
        }
    }

    // Used for MoveTo
    public override OMV.Vector3 PIDTarget {
        set { _PIDTarget = value; }
    }
    public override bool PIDActive {
        set { _usePID = value; }
    }
    public override float PIDTau {
        set { _PIDTau = value; }
    }

    // Used for llSetHoverHeight and maybe vehicle height
    // Hover Height will override MoveTo target's Z
    public override bool PIDHoverActive {
        set { _useHoverPID = value; }
    }
    public override float PIDHoverHeight {
        set { _PIDHoverHeight = value; }
    }
    public override PIDHoverType PIDHoverType {
        set { _PIDHoverType = value; }
    }
    public override float PIDHoverTau {
        set { _PIDHoverTao = value; }
    }

    // For RotLookAt
    public override OMV.Quaternion APIDTarget { set { return; } }
    public override bool APIDActive { set { return; } }
    public override float APIDStrength { set { return; } }
    public override float APIDDamping { set { return; } }

    public override void AddForce(OMV.Vector3 force, bool pushforce)
    {
        // Since this force is being applied in only one step, make this a force per second.
        OMV.Vector3 addForce = force / PhysicsScene.LastTimeStep;
        AddForce(addForce, pushforce, false);
    }
    private void AddForce(OMV.Vector3 force, bool pushforce, bool inTaintTime) {
        if (force.IsFinite())
        {
            OMV.Vector3 addForce = Util.ClampV(force, BSParam.MaxAddForceMagnitude);
            // DetailLog("{0},BSCharacter.addForce,call,force={1}", LocalID, addForce);

            PhysicsScene.TaintedObject(inTaintTime, "BSCharacter.AddForce", delegate()
            {
                // Bullet adds this central force to the total force for this tick
                // DetailLog("{0},BSCharacter.addForce,taint,force={1}", LocalID, addForce);
                if (PhysBody.HasPhysicalBody)
                {
                    PhysicsScene.PE.ApplyCentralForce(PhysBody, addForce);
                }
            });
        }
        else
        {
            m_log.WarnFormat("{0}: Got a NaN force applied to a character. LocalID={1}", LogHeader, LocalID);
            return;
        }
    }

    public override void AddAngularForce(OMV.Vector3 force, bool pushforce) {
    }
    public override void SetMomentum(OMV.Vector3 momentum) {
    }

    private OMV.Vector3 ComputeAvatarScale(OMV.Vector3 size)
    {
        OMV.Vector3 newScale;
        
        // Bullet's capsule total height is the "passed height + radius * 2";
        // The base capsule is 1 diameter and 2 height (passed radius=0.5, passed height = 1)
        // The number we pass in for 'scaling' is the multiplier to get that base
        //     shape to be the size desired.
        // So, when creating the scale for the avatar height, we take the passed height
        //     (size.Z) and remove the caps.
        // Another oddity of the Bullet capsule implementation is that it presumes the Y
        //     dimension is the radius of the capsule. Even though some of the code allows
        //     for a asymmetrical capsule, other parts of the code presume it is cylindrical.

        // Scale is multiplier of radius with one of "0.5"
        newScale.X = size.X / 2f;
        newScale.Y = size.Y / 2f;

        // The total scale height is the central cylindar plus the caps on the two ends.
        newScale.Z = (size.Z + (Math.Min(size.X, size.Y) * 2)) / 2f;
        // If smaller than the endcaps, just fake like we're almost that small
        if (newScale.Z < 0)
            newScale.Z = 0.1f;

        return newScale;
    }

    // set _avatarVolume and _mass based on capsule size, _density and Scale
    private void ComputeAvatarVolumeAndMass()
    {
        _avatarVolume = (float)(
                        Math.PI
                        * Size.X / 2f
                        * Size.Y / 2f    // the area of capsule cylinder
                        * Size.Z         // times height of capsule cylinder
                      + 1.33333333f
                        * Math.PI
                        * Size.X / 2f
                        * Math.Min(Size.X, Size.Y) / 2
                        * Size.Y / 2f    // plus the volume of the capsule end caps
                        );
        _mass = Density * BSParam.DensityScaleFactor * _avatarVolume;
    }

    // The physics engine says that properties have updated. Update same and inform
    // the world that things have changed.
    public override void UpdateProperties(EntityProperties entprop)
    {
        _position = entprop.Position;
        _orientation = entprop.Rotation;

        // Smooth velocity. OpenSimulator is VERY sensitive to changes in velocity of the avatar
        //    and will send agent updates to the clients if velocity changes by more than
        //    0.001m/s. Bullet introduces a lot of jitter in the velocity which causes many
        //    extra updates.
        if (!entprop.Velocity.ApproxEquals(_velocity, 0.1f))
            _velocity = entprop.Velocity;

        _acceleration = entprop.Acceleration;
        _rotationalVelocity = entprop.RotationalVelocity;

        // Do some sanity checking for the avatar. Make sure it's above ground and inbounds.
        if (PositionSanityCheck(true))
        {
            DetailLog("{0},BSCharacter.UpdateProperties,updatePosForSanity,pos={1}", LocalID, _position);
            entprop.Position = _position;
        }

        // remember the current and last set values
        LastEntityProperties = CurrentEntityProperties;
        CurrentEntityProperties = entprop;

        // Tell the linkset about value changes
        // Linkset.UpdateProperties(UpdatedProperties.EntPropUpdates, this);

        // Avatars don't report their changes the usual way. Changes are checked for in the heartbeat loop.
        // base.RequestPhysicsterseUpdate();

        DetailLog("{0},BSCharacter.UpdateProperties,call,pos={1},orient={2},vel={3},accel={4},rotVel={5}",
                LocalID, _position, _orientation, _velocity, _acceleration, _rotationalVelocity);
    }
}
}
