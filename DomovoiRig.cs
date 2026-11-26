namespace BatteryMonitor;

// Independent, damped angular joints. The original anchor/pivot solver
// still controls the whole body. These are secondary animation springs,
// not a full constraint-based rigid-body engine.
internal sealed class DomovoiRig
{
    private sealed class Spring
    {
        internal double Value, Velocity;
        internal void Step(double goal, double stiffness, double damping, double dt, double limit)
        {
            int count = Math.Max(1, (int)Math.Ceiling(dt / .005));
            double h = dt / count;
            for (int i = 0; i < count; i++)
            {
                Velocity += (stiffness * (goal - Value) - damping * Velocity) * h;
                Value += Velocity * h;
                if (Math.Abs(Value) > limit)
                {
                    Value = Math.Clamp(Value, -limit, limit);
                    if (Math.Sign(Velocity) == Math.Sign(Value)) Velocity = 0;
                }
            }
        }
        internal void Reset() { Value = Velocity = 0; }
    }
    private readonly Spring _leftArm = new(), _rightArm = new(), _leftLeg = new(), _rightLeg = new();
    private readonly Spring _head = new(), _hat = new(), _beard = new();
    internal void Reset()
    {
        _leftArm.Reset(); _rightArm.Reset(); _leftLeg.Reset(); _rightLeg.Reset();
        _head.Reset(); _hat.Reset(); _beard.Reset();
    }

    internal DomovoiPose Apply(DomovoiPose pose, double dt, bool held, bool enabled,
        DomovoiPart grip, double angle, double angularVelocity, double ax, int facing)
    {
        if (!enabled) { Reset(); return pose; }
        double radians = angle * Math.PI / 180;
        double lean = held ? -Math.Sin(radians) * 26 : 0;
        double inverted = held ? (1 - Math.Cos(radians)) * .5 : 0;
        double force = held ? Math.Clamp(-ax / 450d, -12, 12) : 0;
        double lag = Math.Clamp(-angularVelocity * .055 * facing, -22, 22);
        double HeldJoint(DomovoiPart part, double value) => held && grip == part ? value * .15 : value;

        _leftArm.Step(HeldJoint(DomovoiPart.LeftHand, lean + inverted * 25 + lag + force), 95, 13, dt, 42);
        _rightArm.Step(HeldJoint(DomovoiPart.RightHand, lean - inverted * 25 + lag * .75 + force), 72, 11, dt, 42);
        _leftLeg.Step(HeldJoint(DomovoiPart.LeftFoot, lean * .6 + inverted * 13 + lag * .6), 105, 14, dt, 29);
        _rightLeg.Step(HeldJoint(DomovoiPart.RightFoot, lean * .6 - inverted * 13 + lag * .8), 82, 12, dt, 29);
        _head.Step(HeldJoint(DomovoiPart.Head, lag * .35 + force * .25), 120, 17, dt, 11);
        _hat.Step(HeldJoint(DomovoiPart.Hat, lag * .55 + force * .4), 65, 9, dt, 15);
        _beard.Step(HeldJoint(DomovoiPart.Beard, lag * .75 + force * .45), 49, 8, dt, 19);
        return pose with {
            LeftArm = pose.LeftArm + (float)_leftArm.Value,
            RightArm = pose.RightArm + (float)_rightArm.Value,
            LeftLeg = pose.LeftLeg + (float)_leftLeg.Value,
            RightLeg = pose.RightLeg + (float)_rightLeg.Value,
            Head = pose.Head + (float)_head.Value,
            Hat = (float)_hat.Value, Beard = (float)_beard.Value
        };
    }
}
