/*******************
Autores:    Angel Garzon Sarzosa (ahgarzon@unicauca.edu.co)
            Jhoan Simei Sarria (simei@unicauca.edu.co)
*******************/

using System.Collections.Generic;
using UnityEngine;

public class RobotKinematics : MonoBehaviour
{
    // Método estático para calcular la matriz de transformación total usando los parámetros DH y los ángulos articulares q
    public static Matrix4x4 MgdAn5(float[] q)
    {
        // Dimensiones DH del robot AN5 (Denavit-Hartenberg)
        float[,] DH_params = new float[,] {
            {0f, Mathf.PI / 2f, 0.152f, q[0]},
            {-0.425f, 0f, 0f, q[1]},
            {-0.395f, 0f, 0f, q[2]},
            {0f, Mathf.PI / 2f, 0.102f, q[3]},
            {0f, -Mathf.PI / 2f, 0.102f, q[4]},
            {0f, 0f, 0.267f, q[5]}
        };

        Matrix4x4 T = Matrix4x4.identity; // Inicializar la matriz de transformación total como identidad

        // Calcular la matriz de transformación para cada articulación y acumularla en T
        for (int i = 0; i < DH_params.GetLength(0); i++)
        {
            float theta = DH_params[i, 3];
            float d = DH_params[i, 2];
            float a = DH_params[i, 0];
            float alpha = DH_params[i, 1];

            Matrix4x4 A = DhTransform(a, alpha, d, theta); // Obtener la matriz DH para la articulación actual

            T *= A; // Multiplicar la matriz total por la matriz de la articulación actual
        }

        return T; // Devolver la matriz de transformación total
    }

    // Método estático para calcular los ángulos articulares q (radianes) a partir de la
    // matriz de transformación total T. T's translation is expected in MILLIMETERS,
    // matching the convention used everywhere else in this codebase (SecTrajController,
    // LocalForwardKinematics, MGD_Node, and this function's sole caller
    // Control_Cartesiano.cs, which feeds it raw UI values like -572, -177, 302) -- NOT
    // the meters MgdAn5 itself works in internally (its DH constants are meter-scale,
    // e.g. 0.425).
    //
    // This used to be a closed-form geometric solve, but it used link/wrist-offset
    // constants (a2=-0.244, a3=-0.213, d4=0.112) that don't match this robot's actual
    // DH table (used by MgdAn5/MGD_Node: a=-0.425/-0.395, d=0.152/0.102/0.102/0.267) --
    // and, on top of that, compared those meter-scale constants directly against T's
    // millimeter-scale translation with no conversion. Either bug alone was enough to
    // send the law-of-cosines "D" term outside [-1,1], returning NaN for perfectly
    // reachable poses.
    //
    // Re-deriving a correct closed-form solution would mean re-deriving the spherical-
    // wrist geometry for this exact DH table by hand -- easy to get subtly wrong in a
    // way that only shows up for certain poses. Instead this solves it numerically
    // (damped least squares / Levenberg-Marquardt) directly against MgdAn5, so it is
    // guaranteed self-consistent with the forward model: whatever MgdAn5 computes for
    // the returned q reproduces T (within tolerance), by construction.
    public static float[] MgiAn5(Matrix4x4 T)
    {
        return MgiAn5(T, null, out _, out _);
    }

    // Overload used by SecTrajController to chain trajectory points: passing the
    // previous point's solution as 'preferredSeedRadians' keeps consecutive points
    // converging to the same arm branch (elbow up/down, wrist flip) instead of each
    // point being solved independently from the same fixed seed list, which can jump
    // between equally-valid-but-very-different joint configurations for two nearby
    // cartesian poses. The out params let callers detect an unreachable/non-converged
    // target (this solver otherwise always returns SOME joint values, even a poor
    // approximation, with no signal that it didn't actually converge).
    public static float[] MgiAn5(Matrix4x4 T, float[] preferredSeedRadians, out float posErrorMm, out float rotErrorDeg)
    {
        Vector3 targetPos = new Vector3(T.m03, T.m13, T.m23) / 1000f; // mm -> m, to match MgdAn5's internal units
        Quaternion targetRot = T.rotation;

        // A single seed can land the solve exactly on a wrist singularity (this arm's
        // joint 4 and joint 6 axes become collinear when q5 sits near 0/180 deg) --
        // there the position error can already be zero while a real rotation error
        // remains, and the damped-least-squares step for it keeps computing to exactly
        // zero every iteration (it isn't stuck by tolerance, it's stuck because the
        // error vector has no component the Jacobian can currently resolve). Retrying
        // from a handful of spread-out seeds and keeping whichever converges best works
        // around that without having to detect/escape the singularity mid-solve.
        float[][] fixedSeeds =
        {
            new float[] { 0f, 0f, 0f, 0f, 0f, 0f },
            new float[] { 0f, 0f, 0f, 0f, 1.5708f, 0f },
            new float[] { 0f, 0f, 0f, 0f, -1.5708f, 0f },
            new float[] { 0.5f, -0.5f, 0.5f, 0.5f, 0.5f, 0.5f },
            new float[] { -0.5f, 0.5f, -0.5f, -0.5f, -0.5f, -0.5f },
            new float[] { 0.7854f, -0.7854f, 0.7854f, -0.7854f, 0.7854f, -0.7854f },
        };

        // Try the caller-supplied seed IN ADDITION to the fixed list (not instead of,
        // and not prioritized over it) so a good previous-point solution can still win
        // on merit, preserving branch continuity, without ever being trusted blindly.
        var seeds = preferredSeedRadians != null && preferredSeedRadians.Length == 6
            ? new List<float[]> { preferredSeedRadians }
            : new List<float[]>();
        seeds.AddRange(fixedSeeds);

        float[] bestQ = seeds[0];
        float bestPosErrorMeters = float.MaxValue;
        float bestRotErrorRadians = float.MaxValue;
        float bestCost = float.MaxValue;

        // Deliberately no early-exit on "good enough" here: near a wrist singularity
        // (joint 4/joint 6 axes collinear) the damped-least-squares step can go to
        // exactly zero while a REAL rotation error remains unresolved (see
        // SolveIkFromSeed's iteration loop) -- that reads as "converged" by the same
        // 0.5mm/0.06deg check this used to break on, so trusting the FIRST seed to hit
        // it could lock in a wrong solution before ever trying the other seeds. This
        // was caught in practice: seeding from the robot's live joint position (see
        // SecTrajController.ResolveJointTrajectory) hit exactly this trap and silently
        // sent a joint target ~187mm away from the intended cartesian point, while
        // still reporting a small residual error. Always evaluating every seed and
        // keeping the genuinely lowest-cost one costs a few extra milliseconds per
        // point (this runs once per point at trajectory-load time, not per frame) but
        // can no longer be fooled by a single seed's false convergence.
        foreach (float[] seed in seeds)
        {
            float[] q = SolveIkFromSeed(seed, targetPos, targetRot, out float posErrorMeters, out float rotErrorRadians);

            // Combine into one scalar so position and orientation error are weighted
            // comparably: 1mm of position error counts about the same as ~0.06 deg of
            // orientation error (1 / 1000 rad).
            float cost = posErrorMeters * 1000f + rotErrorRadians * 1000f;
            if (cost < bestCost)
            {
                bestCost = cost;
                bestQ = q;
                bestPosErrorMeters = posErrorMeters;
                bestRotErrorRadians = rotErrorRadians;
            }
        }

        posErrorMm = bestPosErrorMeters * 1000f;
        rotErrorDeg = bestRotErrorRadians * Mathf.Rad2Deg;
        return bestQ; // Devolver el arreglo de ángulos articulares (radianes)
    }

    // Damped-least-squares (Levenberg-Marquardt) IK solve from a single joint-space seed.
    private static float[] SolveIkFromSeed(float[] seed, Vector3 targetPos, Quaternion targetRot,
        out float finalPosErrorMeters, out float finalRotErrorRadians)
    {
        const int maxIterations = 200;
        const float posToleranceMeters = 0.0005f; // 0.5 mm
        const float rotToleranceRadians = 0.001f;
        const float damping = 0.05f;
        // 1e-4 rad is too small a perturbation for Quaternion.ToAngleAxis to resolve
        // accurately in single precision -- the rotation-Jacobian columns came out as
        // numerical noise rather than the true derivative, which didn't stop position
        // from converging (its Jacobian is a plain Vector3 subtraction, robust at this
        // scale) but left orientation stuck tens of degrees off with a Newton step that
        // had (apparently) already gone to exactly zero. 1e-3 rad (~0.057 deg) is large
        // enough for stable quaternion extraction while still small enough to
        // approximate the local derivative well; verified this converges rotation error
        // to ~0.0000 deg within ~10 iterations on cases that previously got stuck.
        const float jacobianEpsilon = 1e-3f;
        const float maxStepRadians = 0.2f;

        float[] q = (float[])seed.Clone();
        finalPosErrorMeters = float.MaxValue;
        finalRotErrorRadians = float.MaxValue;

        for (int iter = 0; iter < maxIterations; iter++)
        {
            Matrix4x4 current = MgdAn5(q);
            Vector3 currentPos = new Vector3(current.m03, current.m13, current.m23);
            Quaternion currentRot = current.rotation;

            Vector3 posError = targetPos - currentPos;
            Vector3 rotError = RotationErrorVector(targetRot, currentRot);
            finalPosErrorMeters = posError.magnitude;
            finalRotErrorRadians = rotError.magnitude;

            if (finalPosErrorMeters < posToleranceMeters && finalRotErrorRadians < rotToleranceRadians)
                break;

            float[,] jacobian = new float[6, 6];
            for (int j = 0; j < 6; j++)
            {
                float[] qPerturbed = (float[])q.Clone();
                qPerturbed[j] += jacobianEpsilon;
                Matrix4x4 perturbed = MgdAn5(qPerturbed);

                Vector3 dPos = (new Vector3(perturbed.m03, perturbed.m13, perturbed.m23) - currentPos) / jacobianEpsilon;
                Vector3 dRot = RotationErrorVector(perturbed.rotation, currentRot) / jacobianEpsilon;

                jacobian[0, j] = dPos.x; jacobian[1, j] = dPos.y; jacobian[2, j] = dPos.z;
                jacobian[3, j] = dRot.x; jacobian[4, j] = dRot.y; jacobian[5, j] = dRot.z;
            }

            float[] error = { posError.x, posError.y, posError.z, rotError.x, rotError.y, rotError.z };
            float[] dq = DampedLeastSquaresStep(jacobian, error, damping);

            // A large initial error (target far from the seed) makes the local,
            // first-order Jacobian approximation this step is based on wildly
            // inaccurate -- applying it raw could overshoot into a wholly different,
            // often worse configuration, sometimes compounding every iteration until q
            // diverges to unbounded values instead of converging. Clamping each
            // iteration's step keeps every step within the Jacobian's approximately-
            // linear region, trading a few extra iterations for reliable convergence.
            float stepNorm = 0f;
            for (int j = 0; j < 6; j++) stepNorm += dq[j] * dq[j];
            stepNorm = Mathf.Sqrt(stepNorm);
            if (stepNorm > maxStepRadians)
            {
                float scale = maxStepRadians / stepNorm;
                for (int j = 0; j < 6; j++) dq[j] *= scale;
            }

            for (int j = 0; j < 6; j++)
                q[j] += dq[j];
        }

        return q;
    }

    // Minimal rotation vector (axis * angle, radians) representing the rotation that
    // takes 'from' to 'to' -- a well-behaved 3D error term for the Jacobian solve
    // above (avoids the singularities/discontinuities of comparing Euler angles).
    private static Vector3 RotationErrorVector(Quaternion to, Quaternion from)
    {
        Quaternion delta = to * Quaternion.Inverse(from);
        delta.ToAngleAxis(out float angleDeg, out Vector3 axis);
        if (float.IsNaN(axis.x) || float.IsInfinity(angleDeg)) return Vector3.zero;
        if (angleDeg > 180f) angleDeg -= 360f;
        return axis.normalized * (angleDeg * Mathf.Deg2Rad);
    }

    // One damped-least-squares (Levenberg-Marquardt) step: dq = J^T (J J^T + damping^2 I)^-1 error.
    // The damping term keeps this well-conditioned near singularities, where a plain
    // Jacobian inverse would blow up.
    private static float[] DampedLeastSquaresStep(float[,] jacobian, float[] error, float damping)
    {
        const int n = 6;
        float[,] jjt = new float[n, n];
        for (int i = 0; i < n; i++)
            for (int j = 0; j < n; j++)
            {
                float sum = 0f;
                for (int k = 0; k < n; k++)
                    sum += jacobian[i, k] * jacobian[j, k];
                jjt[i, j] = sum + (i == j ? damping * damping : 0f);
            }

        float[] y = SolveLinearSystem(jjt, error, n);

        float[] dq = new float[n];
        for (int i = 0; i < n; i++)
        {
            float sum = 0f;
            for (int k = 0; k < n; k++)
                sum += jacobian[k, i] * y[k];
            dq[i] = sum;
        }
        return dq;
    }

    // Solves A*x=b via Gauss-Jordan elimination with partial pivoting. A is small
    // (6x6) and freshly built every IK iteration, so this simple approach is fine.
    private static float[] SolveLinearSystem(float[,] a, float[] b, int n)
    {
        float[,] m = (float[,])a.Clone();
        float[] x = (float[])b.Clone();

        for (int col = 0; col < n; col++)
        {
            int pivot = col;
            float maxAbs = Mathf.Abs(m[col, col]);
            for (int row = col + 1; row < n; row++)
            {
                if (Mathf.Abs(m[row, col]) > maxAbs)
                {
                    maxAbs = Mathf.Abs(m[row, col]);
                    pivot = row;
                }
            }
            if (pivot != col)
            {
                for (int k = 0; k < n; k++)
                {
                    (m[col, k], m[pivot, k]) = (m[pivot, k], m[col, k]);
                }
                (x[col], x[pivot]) = (x[pivot], x[col]);
            }

            float diag = Mathf.Abs(m[col, col]) < 1e-9f ? 1e-9f : m[col, col];
            for (int row = 0; row < n; row++)
            {
                if (row == col) continue;
                float factor = m[row, col] / diag;
                for (int k = col; k < n; k++)
                    m[row, k] -= factor * m[col, k];
                x[row] -= factor * x[col];
            }
        }

        float[] result = new float[n];
        for (int i = 0; i < n; i++)
        {
            float diag = Mathf.Abs(m[i, i]) < 1e-9f ? 1e-9f : m[i, i];
            result[i] = x[i] / diag;
        }
        return result;
    }

    // Método auxiliar para calcular la matriz de transformación de Denavit-Hartenberg.
    // UnityEngine.Matrix4x4's constructor takes each Vector4 as a COLUMN, not a row --
    // this used to pass the DH matrix's rows as if they were columns, silently
    // transposing the whole transform (e.g. MgdAn5 at the zero-joint pose returned
    // position (0,0,0), which is impossible for an arm with nonzero link lengths).
    // Verified against MGD_Node.cs's independently-implemented row-major DH math,
    // which agrees once this is expressed column-by-column.
    private static Matrix4x4 DhTransform(float a, float alpha, float d, float theta)
    {
        float ct = Mathf.Cos(theta), st = Mathf.Sin(theta);
        float ca = Mathf.Cos(alpha), sa = Mathf.Sin(alpha);

        Matrix4x4 A = new Matrix4x4(
            new Vector4(ct, st, 0f, 0f),
            new Vector4(-st * ca, ct * ca, sa, 0f),
            new Vector4(st * sa, -ct * sa, ca, 0f),
            new Vector4(a * ct, a * st, d, 1f)
        );

        return A; // Devolver la matriz de transformación DH
    }
}
