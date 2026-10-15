namespace CAHFS_Recharges.Hiwu
{
    /// Runs one TraceFirst order. Does not read HIWU files and is not called by ingest.
    public sealed class HiwuCoflOrderService
    {
        private readonly HiwuCoflClient _client;

        public HiwuCoflOrderService(HiwuCoflClient client)
        {
            _client = client;
        }

        public async Task<HiwuCoflOrderResult> ExecuteAsync(HiwuCoflOrder order, CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(order);
            var validationError = Validate(order);
            if (validationError != null)
            {
                return new HiwuCoflOrderResult
                {
                    Status = HiwuCoflOrderResult.Failed,
                    ErrorMessage = validationError,
                    AccessionId = order.AccessionId,
                    AnimalId = order.AnimalId,
                    SpecimenId = order.SpecimenId
                };
            }

            if (IsFileOrder(order))
                return await ExecuteFileOrderAsync(order, cancellationToken);

            var steps = new List<HiwuCoflStepResult>();
            var accessionId = order.AccessionId;
            var animalId = order.AnimalId;
            var specimenId = order.SpecimenId;

            if (WantsAccession(order.Operation) && accessionId == null)
            {
                var call = await _client.CreateAccessionAsync(order.Accession!, cancellationToken);
                var step = ToStep("Accession", call, requireId: true);
                steps.Add(step);
                if (!step.Succeeded)
                    return Finish(steps, accessionId, animalId, specimenId);
                accessionId = step.ResourceId;
            }

            if (WantsAnimal(order.Operation) && animalId == null)
            {
                var animal = order.Animal! with { AccessionId = accessionId ?? order.Animal.AccessionId };
                var call = await _client.CreateAnimalAsync(animal, cancellationToken);
                var step = ToStep("Animal", call, requireId: true);
                steps.Add(step);
                if (!step.Succeeded)
                    return Finish(steps, accessionId, animalId, specimenId);
                animalId = step.ResourceId;
            }

            if (WantsSpecimen(order.Operation) && specimenId == null)
            {
                var specimen = order.Specimen! with { AnimalId = animalId ?? order.Specimen.AnimalId };
                var call = await _client.CreateSpecimenAsync(specimen, accessionId ?? 0, cancellationToken);
                var step = ToStep("Specimen", call, requireId: true);
                steps.Add(step);
                if (!step.Succeeded)
                    return Finish(steps, accessionId, animalId, specimenId);
                specimenId = step.ResourceId;
            }

            if (WantsTests(order.Operation))
            {
                var call = await _client.AssignTestsAsync(specimenId!.Value, order.TestScheduleIds, cancellationToken);
                steps.Add(ToStep("Tests", call, requireId: false));
            }

            return Finish(steps, accessionId, animalId, specimenId);
        }

        private async Task<HiwuCoflOrderResult> ExecuteFileOrderAsync(
            HiwuCoflOrder order,
            CancellationToken cancellationToken)
        {
            var steps = new List<HiwuCoflStepResult>();
            var animalIds = new List<long>();
            var specimenIds = new List<long>();
            var accessionId = order.AccessionId;

            if (accessionId == null)
            {
                var call = await _client.CreateAccessionAsync(order.Accession!, cancellationToken);
                var step = ToStep("Accession", call, requireId: true);
                steps.Add(step);
                if (!step.Succeeded)
                    return Finish(steps, accessionId, Array.Empty<long>(), Array.Empty<long>());
                accessionId = step.ResourceId;
            }

            foreach (var sample in order.Samples)
            {
                var label = string.IsNullOrWhiteSpace(sample.SampleKey) ? "" : " " + sample.SampleKey.Trim();
                var animalId = sample.AnimalId;
                if (animalId == null)
                {
                    var animal = sample.Animal! with { AccessionId = accessionId!.Value };
                    var call = await _client.CreateAnimalAsync(animal, cancellationToken);
                    var step = ToStep("Animal" + label, call, requireId: true);
                    steps.Add(step);
                    if (!step.Succeeded)
                    {
                        if (IsRateLimit(step.ErrorMessage))
                            break;
                        continue;
                    }
                    animalId = step.ResourceId;
                }

                animalIds.Add(animalId!.Value);
                var specimenId = sample.SpecimenId;
                if (specimenId == null)
                {
                    var specimen = sample.Specimen! with { AnimalId = animalId.Value };
                    var call = await _client.CreateSpecimenAsync(specimen, accessionId ?? 0, cancellationToken);
                    var step = ToStep("Specimen" + label, call, requireId: true);
                    steps.Add(step);
                    if (!step.Succeeded)
                    {
                        if (IsRateLimit(step.ErrorMessage))
                            break;
                        continue;
                    }
                    specimenId = step.ResourceId;
                }

                specimenIds.Add(specimenId!.Value);
                if (sample.TestScheduleIds.Count == 0)
                    continue;

                var tests = await _client.AssignTestsAsync(specimenId.Value, sample.TestScheduleIds, cancellationToken);
                var testStep = ToStep("Tests" + label, tests, requireId: false);
                steps.Add(testStep);
                if (!testStep.Succeeded && IsRateLimit(testStep.ErrorMessage))
                    break;
            }

            return Finish(steps, accessionId, animalIds, specimenIds);
        }

        private static HiwuCoflOrderResult Finish(
            List<HiwuCoflStepResult> steps,
            long? accessionId,
            long? animalId,
            long? specimenId)
        {
            var anyFailed = steps.Any(step => !step.Succeeded);
            var anySucceeded = steps.Any(step => step.Succeeded);
            var status = anyFailed
                ? anySucceeded ? HiwuCoflOrderResult.Partial : HiwuCoflOrderResult.Failed
                : HiwuCoflOrderResult.Completed;
            return new HiwuCoflOrderResult
            {
                Status = status,
                AccessionId = accessionId,
                AnimalId = animalId,
                SpecimenId = specimenId,
                AnimalIds = animalId == null ? [] : [animalId.Value],
                SpecimenIds = specimenId == null ? [] : [specimenId.Value],
                ErrorMessage = steps.LastOrDefault(step => !step.Succeeded)?.ErrorMessage,
                Steps = steps
            };
        }

        private static HiwuCoflOrderResult Finish(
            List<HiwuCoflStepResult> steps,
            long? accessionId,
            IReadOnlyList<long> animalIds,
            IReadOnlyList<long> specimenIds)
        {
            var anyFailed = steps.Any(step => !step.Succeeded);
            var anySucceeded = steps.Any(step => step.Succeeded);
            var status = anyFailed
                ? anySucceeded ? HiwuCoflOrderResult.Partial : HiwuCoflOrderResult.Failed
                : HiwuCoflOrderResult.Completed;
            return new HiwuCoflOrderResult
            {
                Status = status,
                AccessionId = accessionId,
                AnimalId = animalIds.Count == 1 ? animalIds[0] : null,
                SpecimenId = specimenIds.Count == 1 ? specimenIds[0] : null,
                AnimalIds = animalIds,
                SpecimenIds = specimenIds,
                ErrorMessage = steps.LastOrDefault(step => !step.Succeeded)?.ErrorMessage,
                Steps = steps
            };
        }

        private static bool IsRateLimit(string? message) =>
            message != null && message.Contains("rate limit", StringComparison.OrdinalIgnoreCase);

        private static HiwuCoflStepResult ToStep(string step, HiwuCoflCallResult call, bool requireId)
        {
            var succeeded = call.Succeeded && (!requireId || call.ResourceId != null);
            var error = call.ErrorMessage;
            if (call.Succeeded && requireId && call.ResourceId == null)
                error = "TraceFirst did not return an id.";

            return new HiwuCoflStepResult
            {
                Step = step,
                ApiVersion = call.ApiVersion,
                Succeeded = succeeded,
                StatusCode = call.StatusCode,
                ResourceId = call.ResourceId,
                ErrorMessage = succeeded ? null : error
            };
        }

        private static string? Validate(HiwuCoflOrder order)
        {
            if (IsFileOrder(order))
                return ValidateFileOrder(order);

            if (WantsAccession(order.Operation) && order.AccessionId == null)
            {
                if (order.Accession == null || order.Accession.LabId <= 0 || order.Accession.ClientId <= 0)
                    return "Accession requires lab_id and client_id, or an existing accession id.";
            }

            if (WantsAnimal(order.Operation) && order.AnimalId == null)
            {
                if (order.Animal == null)
                    return "Animal fields are required.";
                if (order.AccessionId == null && order.Accession == null && order.Animal.AccessionId <= 0)
                    return "Animal requires an accession id.";
            }

            if (WantsSpecimen(order.Operation) && order.SpecimenId == null)
            {
                if (order.Specimen == null)
                    return "Specimen fields are required.";
                if (order.AnimalId == null && order.Animal == null && order.Specimen.AnimalId <= 0)
                    return "Specimen requires an animal id.";
            }

            if (WantsTests(order.Operation))
            {
                if (order.TestScheduleIds == null || order.TestScheduleIds.Count == 0 || order.TestScheduleIds.Any(id => id <= 0))
                    return "At least one test schedule id is required.";
                if (order.SpecimenId == null && order.Specimen == null)
                    return "Tests require a specimen id.";
            }

            return null;
        }

        private static string? ValidateFileOrder(HiwuCoflOrder order)
        {
            if (order.AccessionId == null && (order.Accession == null || order.Accession.LabId <= 0 || order.Accession.ClientId <= 0))
                return "Accession requires lab_id and client_id, or an existing accession id.";

            for (var index = 0; index < order.Samples.Count; index++)
            {
                var sample = order.Samples[index];
                var label = string.IsNullOrWhiteSpace(sample.SampleKey) ? "Sample " + (index + 1) : sample.SampleKey.Trim();
                if (sample.AnimalId == null && sample.Animal == null)
                    return label + " requires animal fields.";
                if (sample.SpecimenId == null && sample.Specimen == null)
                    return label + " requires specimen fields.";
                if (sample.TestScheduleIds.Any(id => id <= 0))
                    return label + " has a test schedule id that is not valid.";
            }

            return null;
        }

        private static bool IsFileOrder(HiwuCoflOrder order) =>
            order.Operation == HiwuCoflOperation.CreateOrder && order.Samples.Count > 0;

        private static bool WantsAccession(HiwuCoflOperation operation) =>
            operation is HiwuCoflOperation.CreateAccession or HiwuCoflOperation.CreateOrder;

        private static bool WantsAnimal(HiwuCoflOperation operation) =>
            operation is HiwuCoflOperation.CreateAnimal or HiwuCoflOperation.CreateOrder;

        private static bool WantsSpecimen(HiwuCoflOperation operation) =>
            operation is HiwuCoflOperation.CreateSpecimen
                or HiwuCoflOperation.CreateSpecimenAndAssignTests
                or HiwuCoflOperation.CreateOrder;

        private static bool WantsTests(HiwuCoflOperation operation) =>
            operation is HiwuCoflOperation.AssignTests
                or HiwuCoflOperation.CreateSpecimenAndAssignTests
                or HiwuCoflOperation.CreateOrder;
    }
}
