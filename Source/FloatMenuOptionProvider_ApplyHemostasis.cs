using RimWorld;
using Verse;
using Verse.AI;

namespace FieldHemostasis
{
    public sealed class FloatMenuOptionProvider_ApplyHemostasis
        : FloatMenuOptionProvider
    {
        protected override bool Drafted => true;
        protected override bool Undrafted => true;
        protected override bool Multiselect => false;

        protected override FloatMenuOption GetSingleOptionFor(
            Pawn clickedPawn,
            FloatMenuContext context)
        {
            Pawn actor = context.FirstSelectedPawn;
            if (actor == null
                || clickedPawn == null
                || clickedPawn.Dead
                || !HemostasisUtility.HasUnstabilizedBleeding(clickedPawn))
            {
                return null;
            }

            if (actor.Downed)
            {
                return DisabledOption("FH_Downed".Translate(actor.LabelShort));
            }

            if (actor.InMentalState)
            {
                return DisabledOption(
                    "FH_MentalState".Translate(actor.LabelShort));
            }

            if (!actor.health.capacities.CapableOf(
                    PawnCapacityDefOf.Manipulation))
            {
                return DisabledOption(
                    "FH_NoManipulation".Translate(actor.LabelShort));
            }

            if (actor != clickedPawn
                && !actor.CanReach(
                    clickedPawn,
                    PathEndMode.Touch,
                    Danger.Deadly))
            {
                return DisabledOption(
                    "FH_CannotReach".Translate(
                        actor.LabelShort,
                        clickedPawn.LabelShort));
            }

            if (actor != clickedPawn && !actor.CanReserve(clickedPawn))
            {
                return DisabledOption(
                    "FH_TargetReserved".Translate(clickedPawn.LabelShort));
            }

            FloatMenuOption option = new FloatMenuOption(
                "FH_ApplyHemostasis".Translate(clickedPawn.LabelShort),
                delegate
                {
                    Job job = JobMaker.MakeJob(
                        FieldHemostasisDefOf.FH_ApplyHemostasis,
                        clickedPawn);
                    actor.jobs.TryTakeOrderedJob(job, JobTag.Misc);
                });

            return FloatMenuUtility.DecoratePrioritizedTask(
                option,
                actor,
                clickedPawn);
        }

        private static FloatMenuOption DisabledOption(TaggedString label)
        {
            return new FloatMenuOption(label, null);
        }
    }
}
