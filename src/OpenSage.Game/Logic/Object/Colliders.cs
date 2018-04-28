﻿using System;
using System.Numerics;
using OpenSage.Graphics.Cameras;
using OpenSage.Mathematics;

namespace OpenSage.Logic.Object
{
    public abstract class Collider
    {
        protected readonly Transform Transform;

        protected Collider(Transform transform)
        {
            Transform = transform;
        }

        public bool Intersects(in Ray ray, out float depth)
        {
            var transformedRay = ray.Transform(Transform.MatrixInverse);

            if (IntersectsTransformedRay(transformedRay, out depth))
            {
                // Assumes uniform scaling
                depth *= Transform.Scale;
                return true;
            }

            return false;
        }

        public abstract bool Intersects(in BoundingFrustum frustum);

        protected abstract bool IntersectsTransformedRay(in Ray ray, out float depth);

        public abstract Rectangle GetBoundingRectangle(PerspectiveCamera camera);

        public static Collider Create(ObjectDefinition definition, Transform transform)
        {
            switch (definition.Geometry)
            {
                case ObjectGeometry.Box:
                    return new BoxCollider(definition, transform);

                case ObjectGeometry.Sphere:
                    return new SphereCollider(definition, transform);

                case ObjectGeometry.Cylinder:
                    return new CylinderCollider(definition, transform);

                case ObjectGeometry.None:
                    return null;

                default:
                    throw new ArgumentOutOfRangeException();
            }
        }
    }

    public class BoxCollider : Collider
    {
        private readonly BoundingBox _bounds;

        public BoxCollider(ObjectDefinition def, Transform transform)
            : base(transform)
        {
            var min = new Vector3(-def.GeometryMajorRadius, -def.GeometryMinorRadius, 0);
            var max = new Vector3(def.GeometryMajorRadius, def.GeometryMinorRadius, def.GeometryHeight);
            _bounds = new BoundingBox(min, max);
        }

        protected override bool IntersectsTransformedRay(in Ray transformedRay, out float depth)
        {
            return transformedRay.Intersects(_bounds, out depth);
        }

        public override bool Intersects(in BoundingFrustum frustum)
        {
            var worldBounds = _bounds.Transform(Transform.Matrix);
            return frustum.Intersects(worldBounds);
        }

        public override Rectangle GetBoundingRectangle(PerspectiveCamera camera)
        {
            var worldBounds = _bounds.Transform(Transform.Matrix);
            return worldBounds.GetBoundingRectangle(camera);
        }
    }

    public class SphereCollider : Collider
    {
        private readonly BoundingSphere _bounds;

        public SphereCollider(ObjectDefinition def, Transform transform)
            : base(transform)
        {
            _bounds = new BoundingSphere(Vector3.Zero, def.GeometryMajorRadius);
        }

        protected override bool IntersectsTransformedRay(in Ray transformedRay, out float depth)
        {
            return transformedRay.Intersects(_bounds, out depth);
        }

        public override bool Intersects(in BoundingFrustum frustum)
        {
            var worldBounds = _bounds.Transform(Transform.Matrix);
            return frustum.Intersects(worldBounds);
        }

        public override Rectangle GetBoundingRectangle(PerspectiveCamera camera)
        {
            // TODO: Implement this.
            // Or don't, since Generals has only 4 spherical selectable objects,
            // and all of them are debug objects.
            return new Rectangle(0, 0, 0, 0);
        }
    }

    // TODO: This currently uses a bounding box for collision.
    // It's a half-decent approximation fow now.
    public class CylinderCollider : Collider
    {
        private readonly BoundingBox _bounds;

        public CylinderCollider(ObjectDefinition def, Transform transform)
            : base(transform)
        {
            var radius = def.GeometryMajorRadius;
            var height = def.GeometryHeight;

            _bounds = new BoundingBox(
                new Vector3(-radius, -radius, 0),
                new Vector3(radius, radius, height));
        }

        protected override bool IntersectsTransformedRay(in Ray transformedRay, out float depth)
        {
            return transformedRay.Intersects(_bounds, out depth);
        }

        public override bool Intersects(in BoundingFrustum frustum)
        {
            var worldBounds = _bounds.Transform(Transform.Matrix);
            return frustum.Intersects(worldBounds);
        }

        public override Rectangle GetBoundingRectangle(PerspectiveCamera camera)
        {
            var worldBounds = _bounds.Transform(Transform.Matrix);
            return worldBounds.GetBoundingRectangle(camera);
        }
    }
}
