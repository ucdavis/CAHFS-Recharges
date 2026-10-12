namespace CAHFS_Recharges.Hiwu
{
    public enum HiwuCoflOperation
    {
        CreateAccession,
        CreateAnimal,
        CreateSpecimen,
        CreateSpecimenAndAssignTests,
        AssignTests,
        CreateOrder
    }

    public sealed record HiwuCoflAccessionRequest
    {
        public long LabId { get; init; }
        public long ClientId { get; init; }
        public long? UserId { get; init; }
        public long? VetId { get; init; }
        public long? CarrierId { get; init; }
        public string? CarrierRefNum { get; init; }
        public long? OwnerId { get; init; }
        public string? AdditionalInfo { get; init; }
        public string? PreAccessionRef { get; init; }
        public int? Priority { get; init; }
    }

    public sealed record HiwuCoflAnimalTag
    {
        public long TagTypeId { get; init; }
        public required string Num { get; init; }
    }

    public sealed record HiwuCoflAnimalRequest
    {
        public long AccessionId { get; init; }
        public long? BreedId { get; init; }
        public long? SexCodeId { get; init; }
        public long? WeightUomId { get; init; }
        public long? AgeUomId { get; init; }
        public string? Weight { get; init; }
        public string? Age { get; init; }
        public bool? Legal { get; init; }
        public bool? Insured { get; init; }
        public string? Name { get; init; }
        public bool? NonAnimal { get; init; }
        public IReadOnlyList<HiwuCoflAnimalTag> Tags { get; init; } = [];
    }

    public sealed record HiwuCoflSpecimenRequest
    {
        public long AnimalId { get; init; }
        public long? SpecimenTypeId { get; init; }
        public long? SpecimenTypeDescriptionId { get; init; }
        public string? CollectionDate { get; init; }
        public long? TransportMediumId { get; init; }
        public long? ConditionId { get; init; }
        public long? LocationId { get; init; }
        public string? Name { get; init; }
        public long? OriginalSpecimenId { get; init; }
        public string? CaseCoordinatorComment { get; init; }
    }

    public sealed record HiwuCoflReceivingSpecimen
    {
        public long? SpecimenTypeId { get; init; }
        public long? SpecimenTypeDescriptionId { get; init; }
        public string? CollectionDate { get; init; }
        public long? TransportMediumId { get; init; }
        public long? ConditionId { get; init; }
        public string? Name { get; init; }
        public long? LocationId { get; init; }
        public long? OriginalSpecimenId { get; init; }
        public string? CaseCoordinatorComment { get; init; }
    }

    public sealed record HiwuCoflReceivingAnimal
    {
        public long? BreedId { get; init; }
        public long? SexCodeId { get; init; }
        public long? WeightUomId { get; init; }
        public long? AgeUomId { get; init; }
        public string? Weight { get; init; }
        public string? Age { get; init; }
        public bool? Legal { get; init; }
        public bool? Insured { get; init; }
        public string? Name { get; init; }
        public bool? NonAnimal { get; init; }
        public IReadOnlyList<HiwuCoflAnimalTag> Tags { get; init; } = [];
        public IReadOnlyList<HiwuCoflReceivingSpecimen> Specimens { get; init; } = [];
    }

    public sealed record HiwuCoflReceivingRequest
    {
        public long LabId { get; init; }
        public long ClientId { get; init; }
        public long? UserId { get; init; }
        public long? OwnerId { get; init; }
        public int? Priority { get; init; }
        public string? PreAccessionRef { get; init; }
        public long? CarrierId { get; init; }
        public string? CarrierRefNum { get; init; }
        public string? AdditionalInfo { get; init; }
        public IReadOnlyList<HiwuCoflReceivingAnimal> Animals { get; init; } = [];
    }

    public sealed class HiwuCoflReceivedSample
    {
        public string? AnimalName { get; init; }
        public long AnimalId { get; init; }
        public string? SpecimenName { get; init; }
        public long? SpecimenId { get; init; }
    }

    public sealed class HiwuCoflReceivingResult
    {
        public bool Succeeded { get; init; }
        public int StatusCode { get; init; }
        public required string ApiVersion { get; init; }
        public long? AccessionId { get; init; }
        public IReadOnlyList<long> AnimalIds { get; init; } = [];
        public IReadOnlyList<long> SpecimenIds { get; init; } = [];
        public bool SpecimensSent { get; init; }
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<HiwuCoflReceivedSample> Samples { get; init; } = [];
    }

    public sealed class HiwuCoflCallResult
    {
        public bool Succeeded { get; init; }
        public int StatusCode { get; init; }
        public required string ApiVersion { get; init; }
        public long? ResourceId { get; init; }
        public string? ErrorMessage { get; init; }
        public string? ResponseBody { get; init; }
    }

    public sealed class HiwuCoflOrderSample
    {
        public string? SampleKey { get; init; }
        public HiwuCoflAnimalRequest? Animal { get; init; }
        public long? AnimalId { get; init; }
        public HiwuCoflSpecimenRequest? Specimen { get; init; }
        public long? SpecimenId { get; init; }
        public IReadOnlyList<long> TestScheduleIds { get; init; } = [];
    }

    public sealed class HiwuCoflOrder
    {
        public HiwuCoflOperation Operation { get; init; }
        public HiwuCoflAccessionRequest? Accession { get; init; }
        public long? AccessionId { get; init; }
        public HiwuCoflAnimalRequest? Animal { get; init; }
        public long? AnimalId { get; init; }
        public HiwuCoflSpecimenRequest? Specimen { get; init; }
        public long? SpecimenId { get; init; }
        public IReadOnlyList<long> TestScheduleIds { get; init; } = [];
        public IReadOnlyList<HiwuCoflOrderSample> Samples { get; init; } = [];
    }

    public sealed class HiwuCoflStepResult
    {
        public required string Step { get; init; }
        public required string ApiVersion { get; init; }
        public bool Succeeded { get; init; }
        public int StatusCode { get; init; }
        public long? ResourceId { get; init; }
        public string? ErrorMessage { get; init; }
    }

    public sealed class HiwuCoflOrderResult
    {
        public const string Completed = "Completed";
        public const string Partial = "Partial";
        public const string Failed = "Failed";

        public required string Status { get; init; }
        public long? AccessionId { get; init; }
        public long? AnimalId { get; init; }
        public long? SpecimenId { get; init; }
        public IReadOnlyList<long> AnimalIds { get; init; } = [];
        public IReadOnlyList<long> SpecimenIds { get; init; } = [];
        public string? ErrorMessage { get; init; }
        public IReadOnlyList<HiwuCoflStepResult> Steps { get; init; } = [];
    }
}
