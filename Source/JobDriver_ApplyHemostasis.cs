using System.Collections.Generic;
using RimWorld;
using UnityEngine;
using Verse;
using Verse.AI;

namespace FieldHemostasis
{
    public sealed class JobDriver_ApplyHemostasis : JobDriver
    {
        private const int BaseWorkTicksPerBleedingSource = 300;

        private int ticksUntilNextApplication;
        private int workTicksForCurrentSource;
        private int stabilizedSourceCount;

        private Pawn Patient =>
            job.GetTarget(TargetIndex.A).Thing as Pawn;

        public override bool TryMakePreToilReservations(bool errorOnFailed)
        {
            Pawn patient = Patient;
            return patient != null
                && (patient == pawn
                    || pawn.Reserve(
                        patient,
                        job,
                        1,
                        -1,
                        null,
                        errorOnFailed));
        }

        public override void ExposeData()
        {
            base.ExposeData();
            Scribe_Values.Look(
                ref ticksUntilNextApplication,
                "ticksUntilNextApplication");
            Scribe_Values.Look(
                ref workTicksForCurrentSource,
                "workTicksForCurrentSource");
            Scribe_Values.Look(
                ref stabilizedSourceCount,
                "stabilizedSourceCount");
        }

        protected override IEnumerable<Toil> MakeNewToils()
        {
            if (Patient != pawn)
            {
                yield return Toils_Goto.GotoThing(
                    TargetIndex.A,
                    PathEndMode.Touch);
            }

            Toil applyHemostasis =
                ToilMaker.MakeToil("ApplyFieldHemostasis");

            applyHemostasis.initAction = delegate
            {
                BeginNextApplication(applyHemostasis.actor);
            };

            applyHemostasis.tickAction = delegate
            {
                Pawn actor = applyHemostasis.actor;
                Pawn patient = Patient;

                if (patient == null || patient.Dead)
                {
                    actor.jobs.EndCurrentJob(JobCondition.Incompletable);
                    return;
                }

                if (patient != actor)
                {
                    actor.rotationTracker.FaceTarget(patient);
                }

                ticksUntilNextApplication--;
                if (ticksUntilNextApplication > 0)
                {
                    return;
                }

                if (!HemostasisUtility.TryGetWorstUnstabilizedBleedingSource(
                        patient,
                        out Hediff bleedingSource))
                {
                    SendCompletionMessage(patient);
                    actor.jobs.EndCurrentJob(JobCondition.Succeeded);
                    return;
                }

                HemostasisGameComponent component =
                    HemostasisGameComponent.Instance;
                if (component != null
                    && component.AddOrRefresh(
                        bleedingSource,
                        HemostasisDuration.RollTicks()))
                {
                    stabilizedSourceCount++;
                }

                BeginNextApplication(actor);
            };

            applyHemostasis.FailOnCannotTouch(
                TargetIndex.A,
                PathEndMode.Touch);
            applyHemostasis.defaultCompleteMode =
                ToilCompleteMode.Never;
            applyHemostasis.handlingFacing = true;
            applyHemostasis.WithProgressBar(
                TargetIndex.A,
                () => workTicksForCurrentSource <= 0
                    ? 0f
                    : Mathf.Clamp01(
                        1f
                        - (float)ticksUntilNextApplication
                        / workTicksForCurrentSource));
            yield return applyHemostasis;
        }

        private void BeginNextApplication(Pawn actor)
        {
            workTicksForCurrentSource = WorkTicksFor(actor);
            ticksUntilNextApplication = workTicksForCurrentSource;
        }

        private static int WorkTicksFor(Pawn actor)
        {
            float tendSpeed = Mathf.Max(
                0.05f,
                actor.GetStatValue(StatDefOf.MedicalTendSpeed));

            return Mathf.Max(
                1,
                Mathf.RoundToInt(
                    BaseWorkTicksPerBleedingSource / tendSpeed));
        }

        private void SendCompletionMessage(Pawn patient)
        {
            if (stabilizedSourceCount <= 0)
            {
                return;
            }

            if (stabilizedSourceCount == 1)
            {
                Messages.Message(
                    "FH_OneSourceStabilized".Translate(
                        patient.LabelShort),
                    MessageTypeDefOf.PositiveEvent,
                    historical: false);
                return;
            }

            Messages.Message(
                "FH_MultipleSourcesStabilized".Translate(
                    patient.LabelShort,
                    stabilizedSourceCount),
                MessageTypeDefOf.PositiveEvent,
                historical: false);
        }
    }
}
