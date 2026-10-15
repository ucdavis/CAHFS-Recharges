using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace CAHFS_Recharges.Hiwu
{
    public sealed class HiwuCoflSession
    {
        public required string Email { get; init; }

        public required string Token { get; init; }

        public required DateTimeOffset ExpiresAt { get; init; }

        public long UserId { get; init; }
    }

    /// Logs in to TraceFirst and reuses auth_token until shortly before it expires.
    
    public sealed class HiwuCoflClient
    {
        public const string HttpClientName = "HiwuCofl";

        private static readonly TimeSpan RefreshBuffer = TimeSpan.FromMinutes(5);
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly IOptions<HiwuOptions> _options;
        private readonly ILogger<HiwuCoflClient> _logger;
        private readonly SemaphoreSlim _refreshLock = new(1, 1);
        private readonly SemaphoreSlim _postLock = new(1, 1);
        private readonly object _accessionNumberLock = new();
        private readonly Dictionary<long, string> _accessionNumbers = new();
        private HiwuCoflSession? _session;
        private DateTime _nextPostUtc;

        public HiwuCoflClient(
            IHttpClientFactory httpClientFactory,
            IOptions<HiwuOptions> options,
            ILogger<HiwuCoflClient> logger)
        {
            _httpClientFactory = httpClientFactory;
            _options = options;
            _logger = logger;
        }

        public async Task<HiwuCoflSession> GetSessionAsync(CancellationToken cancellationToken = default)
        {
            if (IsCurrent(_session))
                return _session!;

            await _refreshLock.WaitAsync(cancellationToken);
            try
            {
                if (IsCurrent(_session))
                    return _session!;

                _session = await LoginAsync(cancellationToken);
                return _session;
            }
            finally
            {
                _refreshLock.Release();
            }
        }

        public async Task<HttpRequestMessage> CreateRequestAsync(
            HttpMethod method,
            string relativePath,
            CancellationToken cancellationToken = default)
        {
            var session = await GetSessionAsync(cancellationToken);
            var request = new HttpRequestMessage(method, BuildApiUri(_options.Value.Cofl.Website, relativePath));
            request.Headers.TryAddWithoutValidation("X-Api-Email", session.Email);
            request.Headers.TryAddWithoutValidation("X-Api-Token", session.Token);
            return request;
        }

        public async Task<IReadOnlyList<HiwuCoflNamedRecord>> ListNamedAsync(
            string v2Path,
            string v1Path,
            CancellationToken cancellationToken = default)
        {
            var found = new Dictionary<long, HiwuCoflNamedRecord>();
            for (var page = 1; page <= 20; page++)
            {
                var path = v2Path + (v2Path.Contains('?') ? "&" : "?") + "per_page=100&page=" + page;
                var v1 = v1Path + (v1Path.Contains('?') ? "&" : "?") + "per_page=100&page=" + page;
                var response = await GetWithFallbackAsync(path, v1, cancellationToken);
                if (!response.Succeeded)
                    throw new InvalidOperationException(response.ErrorMessage ?? "TraceFirst lookup failed.");

                var pageRecords = HiwuCoflNamedRecord.Read(response.ResponseBody);
                if (pageRecords.Count == 0)
                    break;

                var added = 0;
                foreach (var record in pageRecords)
                {
                    if (found.TryGetValue(record.Id, out var existing))
                    {
                        if (record.Names.Count > existing.Names.Count)
                            found[record.Id] = record;
                        continue;
                    }

                    found.Add(record.Id, record);
                    added++;
                }

                if (added == 0)
                    break;
            }

            return found.Values.ToList();
        }

        public async Task<long?> FindTestScheduleIdAsync(string code, string? longName, CancellationToken cancellationToken = default)
        {
            var id = await SearchListedIdAsync("api/v2/test_schedules", "api/v1/test_schedules", code, longName, cancellationToken);
            if (id == null && !string.IsNullOrWhiteSpace(longName))
                id = await SearchListedIdAsync("api/v2/test_schedules", "api/v1/test_schedules", longName, longName, cancellationToken);
            if (id != null)
                return id;

            id = await SearchListedIdAsync("api/v2/tests", "api/v1/tests", code, longName, cancellationToken);
            if (id == null && !string.IsNullOrWhiteSpace(longName))
                id = await SearchListedIdAsync("api/v2/tests", "api/v1/tests", longName, longName, cancellationToken);
            return id;
        }

        private async Task<long?> SearchListedIdAsync(
            string v2Path,
            string v1Path,
            string search,
            string? longName,
            CancellationToken cancellationToken)
        {
            var query = "?per_page=100&page=1&search=" + Uri.EscapeDataString(search);
            var response = await GetWithFallbackAsync(v2Path + query, v1Path + query, cancellationToken);
            if (!response.Succeeded)
            {
                _logger.LogWarning("HIWU COFL lookup {Path} {Search} failed with status {StatusCode}.", v2Path, search, response.StatusCode);
                return null;
            }

            var id = HiwuCoflNamedRecord.PickListedId(response.ResponseBody, search, longName);
            _logger.LogInformation("HIWU COFL lookup {Path} {Search} resolved id {Id}.", v2Path, search, id);
            return id;
        }

        public async Task<string?> GetAccessionNumberAsync(long accessionId, CancellationToken cancellationToken = default)
        {
            if (accessionId <= 0)
                return null;
            lock (_accessionNumberLock)
            {
                if (_accessionNumbers.TryGetValue(accessionId, out var cached))
                    return cached;
            }

            try
            {
                var shown = await GetWithFallbackAsync(
                    "api/v2/accessions/" + accessionId,
                    "api/v1/accessions/" + accessionId,
                    cancellationToken);
                var number = shown.Succeeded ? ReadAccessionNumber(shown.ResponseBody, accessionId) : null;
                if (string.IsNullOrWhiteSpace(number))
                {
                    var listed = await GetWithFallbackAsync(
                        "api/v2/accessions?per_page=5&accession_id_arr=" + accessionId,
                        "api/v1/accessions?per_page=5&accession_id_arr=" + accessionId,
                        cancellationToken);
                    number = listed.Succeeded ? ReadAccessionNumber(listed.ResponseBody, accessionId) : null;
                }

                if (!string.IsNullOrWhiteSpace(number))
                {
                    lock (_accessionNumberLock)
                        _accessionNumbers[accessionId] = number;
                }
                return number;
            }
            catch (Exception ex) when (ex is HttpRequestException or InvalidOperationException)
            {
                _logger.LogWarning(ex, "HIWU COFL accession number lookup failed for {AccessionId}.", accessionId);
                return null;
            }
        }

        public Task<HiwuCoflCallResult> CreateAccessionAsync(
            HiwuCoflAccessionRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.LabId <= 0 || request.ClientId <= 0)
                throw new ArgumentException("Accession requires lab_id and client_id.");

            var body = new AccessionBody(request);
            return PostWithFallbackAsync(
                "api/v2/accessions",
                new AccessionEnvelope(body),
                "api/v1/accessions",
                new AccessionEnvelope(body),
                cancellationToken);
        }

        public async Task<HiwuCoflReceivingResult> CreateReceivingAccessionAsync(
            HiwuCoflReceivingRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.LabId <= 0 || request.ClientId <= 0)
                throw new ArgumentException("Receiving accession requires lab_id and client_id.");

            var specimensRequested = request.Animals.Any(animal => animal.Specimens.Count > 0);
            var v2 = await PostAsync(
                "api/v2/receiving/new_accession",
                new ReceivingEnvelope<ReceivingV2Accession>(new ReceivingV2Accession(request)),
                cancellationToken);
            if (!IsMissingRoute(v2.Status, v2.Body))
                return ToReceivingResult(v2, "v2", specimensSent: specimensRequested);

            if (request.UserId == null || request.UserId <= 0)
            {
                return new HiwuCoflReceivingResult
                {
                    Succeeded = false,
                    StatusCode = (int)v2.Status,
                    ApiVersion = "v2",
                    SpecimensSent = false,
                    ErrorMessage = "v2 receiving is not available, and v1 receiving requires user_id."
                };
            }

            _logger.LogInformation("HIWU COFL v2 route api/v2/receiving/new_accession is not available. Trying v1.");
            var v1 = await PostAsync(
                "api/v1/receiving/new_accession",
                new ReceivingEnvelope<ReceivingV1Accession>(new ReceivingV1Accession(request)),
                cancellationToken);
            var result = ToReceivingResult(v1, "v1", specimensSent: false);
            if (result.Succeeded && specimensRequested)
            {
                return new HiwuCoflReceivingResult
                {
                    Succeeded = true,
                    StatusCode = result.StatusCode,
                    ApiVersion = result.ApiVersion,
                    AccessionId = result.AccessionId,
                    AnimalIds = result.AnimalIds,
                    SpecimenIds = result.SpecimenIds,
                    SpecimensSent = false,
                    ErrorMessage = "v1 receiving created the accession and animals. Specimens are not part of that call."
                };
            }

            return result;
        }

        public async Task<HiwuCoflReceivingResult> CreateReceivingFileAsync(
            HiwuCoflReceivingRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.LabId <= 0 || request.ClientId <= 0)
                throw new ArgumentException("Receiving accession requires lab_id and client_id.");

            var specimensRequested = request.Animals.Any(animal => animal.Specimens.Count > 0);
            var response = await PostAsync(
                "api/v2/receiving/new_accession",
                new ReceivingEnvelope<ReceivingV2Accession>(new ReceivingV2Accession(request)),
                cancellationToken);
            var result = ToReceivingResult(response, "v2", specimensSent: specimensRequested);
            if (!result.Succeeded || result.AccessionId == null)
            {
                return new HiwuCoflReceivingResult
                {
                    Succeeded = false,
                    StatusCode = result.StatusCode,
                    ApiVersion = result.ApiVersion,
                    ErrorMessage = result.ErrorMessage ?? "TraceFirst did not return an accession id."
                };
            }

            return new HiwuCoflReceivingResult
            {
                Succeeded = true,
                StatusCode = result.StatusCode,
                ApiVersion = result.ApiVersion,
                AccessionId = result.AccessionId,
                AnimalIds = result.AnimalIds,
                SpecimenIds = result.SpecimenIds,
                SpecimensSent = result.SpecimensSent,
                Samples = ReadReceivedSamples(response.Body)
            };
        }

        public static IReadOnlyList<HiwuCoflReceivedSample> ReadReceivedSamples(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return [];

            try
            {
                using var document = JsonDocument.Parse(json);
                var found = new List<HiwuCoflReceivedSample>();
                CollectReceivedAnimals(document.RootElement, found);
                return found;
            }
            catch (JsonException)
            {
                return [];
            }
        }

        private static void CollectReceivedAnimals(JsonElement element, List<HiwuCoflReceivedSample> found)
        {
            if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                    CollectReceivedAnimals(item, found);
                return;
            }

            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "animals" or "animals_attributes" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var animal in property.Value.EnumerateArray())
                        TakeReceivedAnimal(animal, found);
                    continue;
                }

                CollectReceivedAnimals(property.Value, found);
            }
        }

        private static void TakeReceivedAnimal(JsonElement animal, List<HiwuCoflReceivedSample> found)
        {
            if (animal.ValueKind != JsonValueKind.Object || !TryReadId(animal, out var animalId))
                return;

            var animalName = ReadJsonString(animal, "name");
            var specimens = new List<(long Id, string? Name)>();
            CollectReceivedSpecimens(animal, specimens);
            if (specimens.Count == 0)
            {
                found.Add(new HiwuCoflReceivedSample { AnimalName = animalName, AnimalId = animalId });
                return;
            }

            foreach (var specimen in specimens)
            {
                found.Add(new HiwuCoflReceivedSample
                {
                    AnimalName = animalName,
                    AnimalId = animalId,
                    SpecimenName = specimen.Name,
                    SpecimenId = specimen.Id
                });
            }
        }

        private static void CollectReceivedSpecimens(JsonElement element, List<(long Id, string? Name)> specimens)
        {
            if (element.ValueKind != JsonValueKind.Object)
                return;

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is "specimens" or "specimens_attributes" && property.Value.ValueKind == JsonValueKind.Array)
                {
                    foreach (var specimen in property.Value.EnumerateArray())
                    {
                        if (specimen.ValueKind == JsonValueKind.Object && TryReadId(specimen, out var specimenId))
                            specimens.Add((specimenId, ReadJsonString(specimen, "name")));
                    }
                    continue;
                }

                if (property.Value.ValueKind == JsonValueKind.Object && property.Name is not ("animals" or "animals_attributes"))
                    CollectReceivedSpecimens(property.Value, specimens);
            }
        }

        private static bool TryReadId(JsonElement element, out long id)
        {
            if (TryReadLong(element, "id", out id))
                return true;
            if (element.TryGetProperty("attributes", out var attributes) && attributes.ValueKind == JsonValueKind.Object)
                return TryReadLong(attributes, "id", out id);
            return false;
        }

        private static string? ReadJsonString(JsonElement element, string name)
        {
            if (element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String)
            {
                var text = property.GetString()?.Trim();
                if (!string.IsNullOrEmpty(text))
                    return text;
            }

            if (!element.TryGetProperty("attributes", out var attributes) || attributes.ValueKind != JsonValueKind.Object)
                return null;
            if (!attributes.TryGetProperty(name, out var nested) || nested.ValueKind != JsonValueKind.String)
                return null;
            var nestedText = nested.GetString()?.Trim();
            return string.IsNullOrEmpty(nestedText) ? null : nestedText;
        }

        public async Task<long?> FindUnassignedAnimalIdAsync(
            long accessionId,
            string? name,
            IReadOnlySet<long> usedIds,
            CancellationToken cancellationToken = default)
        {
            if (accessionId <= 0 || string.IsNullOrWhiteSpace(name))
                return null;

            var animals = await LoadAnimalsAsync(accessionId, name.Trim(), cancellationToken);
            foreach (var animal in animals)
            {
                if (animal.Id == 0 || usedIds.Contains(animal.Id) || animal.Empty == false)
                    continue;
                return animal.Id;
            }

            return null;
        }

        public async Task<HiwuCoflCallResult> CreateAnimalAsync(
            HiwuCoflAnimalRequest request,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.AccessionId <= 0)
                throw new ArgumentException("Animal requires an accession id.");

            return await PostAnimalAsync(request, cancellationToken);
        }

        private async Task<HiwuCoflCallResult> PostAnimalAsync(
            HiwuCoflAnimalRequest request,
            CancellationToken cancellationToken)
        {
            var response = await PostAsync(
                "api/v1/accessions/new_animal",
                new AnimalEnvelope(new AnimalBody(request, tagsAsAttributes: false)),
                cancellationToken);
            var result = ToResult(response, "v1");
            if (!result.Succeeded)
                return result;

            var animalId = ReadAnimalId(response.Body, request.AccessionId, request.Name) ?? result.ResourceId;
            if (animalId == null || animalId == request.AccessionId)
            {
                return new HiwuCoflCallResult
                {
                    Succeeded = false,
                    StatusCode = result.StatusCode,
                    ApiVersion = result.ApiVersion,
                    ErrorMessage = "TraceFirst did not return an animal id.",
                    ResponseBody = result.ResponseBody
                };
            }

            return new HiwuCoflCallResult
            {
                Succeeded = true,
                StatusCode = result.StatusCode,
                ApiVersion = result.ApiVersion,
                ResourceId = animalId,
                ResponseBody = result.ResponseBody
            };
        }

        private async Task<List<AnimalMatch>> LoadAnimalsAsync(long accessionId, string name, CancellationToken cancellationToken)
        {
            var listed = await GetBodyAsync(
                "api/v2/animals?per_page=100&accession_id=" + accessionId + "&search=" + Uri.EscapeDataString(name),
                cancellationToken);
            return listed.Ok && !string.IsNullOrWhiteSpace(listed.Body)
                ? ReadAnimals(listed.Body, accessionId, name)
                : [];
        }

        private async Task<(bool Ok, string Body)> GetBodyAsync(string relativePath, CancellationToken cancellationToken)
        {
            using var request = await CreateRequestAsync(HttpMethod.Get, relativePath, cancellationToken);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var response = await client.SendAsync(request, cancellationToken);
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            return (response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created, body);
        }

        private static HiwuCoflCallResult ExistingAnimal(long id) =>
            new()
            {
                Succeeded = true,
                StatusCode = (int)HttpStatusCode.OK,
                ApiVersion = "v1",
                ResourceId = id
            };

        private readonly record struct AnimalMatch(long Id, bool? Empty);

        private static List<AnimalMatch> ReadAnimals(string body, long accessionId, string name)
        {
            var found = new List<AnimalMatch>();
            try
            {
                using var document = JsonDocument.Parse(body);
                Walk(document.RootElement);
            }
            catch (JsonException)
            {
                return found;
            }

            return found;

            void Walk(JsonElement element)
            {
                if (element.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in element.EnumerateArray())
                        Walk(item);
                    return;
                }

                if (element.ValueKind != JsonValueKind.Object)
                    return;

                var specimen = element.TryGetProperty("specimen_type_id", out _) || element.TryGetProperty("specimen_type", out _);
                var otherAccession = TryReadLong(element, "accession_id", out var ownerId) && ownerId != accessionId;
                if (!specimen && !otherAccession && AnimalNameMatches(element, name) && TryReadId(element, out var id) && found.All(animal => animal.Id != id))
                    found.Add(new AnimalMatch(id, SpecimenEmpty(element)));

                foreach (var property in element.EnumerateObject())
                {
                    if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                        Walk(property.Value);
                }
            }
        }

        private static bool AnimalNameMatches(JsonElement element, string name)
        {
            if (string.Equals(ReadJsonString(element, "name"), name, StringComparison.OrdinalIgnoreCase)
                || string.Equals(ReadJsonString(element, "num"), name, StringComparison.OrdinalIgnoreCase))
                return true;

            foreach (var listName in new[] { "animal_tags", "tags", "animal_tags_attributes" })
            {
                if (!element.TryGetProperty(listName, out var tags) || tags.ValueKind != JsonValueKind.Array)
                    continue;

                foreach (var tag in tags.EnumerateArray())
                {
                    if (tag.ValueKind == JsonValueKind.Object
                        && string.Equals(ReadJsonString(tag, "num"), name, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool? SpecimenEmpty(JsonElement animal)
        {
            if (TrySpecimenArray(animal, "specimens", out var specimens) || TrySpecimenArray(animal, "specimens_attributes", out specimens))
                return specimens.GetArrayLength() == 0;

            if (animal.TryGetProperty("relationships", out var relationships)
                && relationships.ValueKind == JsonValueKind.Object
                && relationships.TryGetProperty("specimens", out var related)
                && related.ValueKind == JsonValueKind.Object
                && TrySpecimenArray(related, "data", out specimens))
                return specimens.GetArrayLength() == 0;

            return null;
        }

        private static bool TrySpecimenArray(JsonElement element, string name, out JsonElement array)
        {
            if (element.TryGetProperty(name, out array) && array.ValueKind == JsonValueKind.Array)
                return true;

            if (element.TryGetProperty("attributes", out var attributes)
                && attributes.ValueKind == JsonValueKind.Object
                && attributes.TryGetProperty(name, out array)
                && array.ValueKind == JsonValueKind.Array)
                return true;

            array = default;
            return false;
        }

        public async Task<HiwuCoflCallResult> CreateSpecimenAsync(
            HiwuCoflSpecimenRequest request,
            long accessionId,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(request);
            if (request.AnimalId <= 0)
                throw new ArgumentException("Specimen requires an animal id.");

            var response = await PostAsync(
                "api/v1/accessions/new_specimen",
                new SpecimenEnvelope(new SpecimenBody(request)),
                cancellationToken);
            var result = ToResult(response, "v1");
            if (!result.Succeeded)
                return result;

            var specimenId = ReadSpecimenId(response.Body, request.AnimalId, accessionId) ?? result.ResourceId;
            if (specimenId == null || specimenId == request.AnimalId || (accessionId > 0 && specimenId == accessionId))
            {
                return new HiwuCoflCallResult
                {
                    Succeeded = false,
                    StatusCode = result.StatusCode,
                    ApiVersion = result.ApiVersion,
                    ErrorMessage = "TraceFirst did not return a specimen id.",
                    ResponseBody = result.ResponseBody
                };
            }

            return new HiwuCoflCallResult
            {
                Succeeded = true,
                StatusCode = result.StatusCode,
                ApiVersion = result.ApiVersion,
                ResourceId = specimenId,
                ResponseBody = result.ResponseBody
            };
        }

        public async Task<HiwuCoflCallResult> AssignTestsAsync(
            long specimenId,
            IReadOnlyList<long> testScheduleIds,
            CancellationToken cancellationToken = default)
        {
            if (specimenId <= 0)
                throw new ArgumentException("Tests require a specimen id.");
            if (testScheduleIds == null || testScheduleIds.Count == 0 || testScheduleIds.Any(id => id <= 0))
                throw new ArgumentException("At least one test schedule id is required.");

            _logger.LogInformation(
                "HIWU COFL assigning tests to specimen {SpecimenId}: {ScheduleIds}.",
                specimenId,
                string.Join(',', testScheduleIds));
            var response = await PostAsync(
                "api/v1/accessions/add_tests_to_specimen",
                new TestOrderV1Body(specimenId, testScheduleIds),
                cancellationToken);
            var result = ToResult(response, "v1");
            if (result.Succeeded || !IsAlreadyAssigned(result.ErrorMessage))
                return result;

            return new HiwuCoflCallResult
            {
                Succeeded = true,
                StatusCode = result.StatusCode,
                ApiVersion = result.ApiVersion,
                ResponseBody = result.ResponseBody
            };
        }

        private async Task<HiwuCoflCallResult> PostWithFallbackAsync(
            string v2Path,
            object v2Payload,
            string v1Path,
            object v1Payload,
            CancellationToken cancellationToken)
        {
            var v2 = await PostAsync(v2Path, v2Payload, cancellationToken);
            if (v2.Status is HttpStatusCode.OK or HttpStatusCode.Created || IsRateLimited(v2.Status, v2.Body))
                return ToResult(v2, "v2");
            if (!IsMissingRoute(v2.Status, v2.Body) && !IsAccessDenied(v2.Status))
                return ToResult(v2, "v2");

            if (IsAccessDenied(v2.Status))
                _logger.LogInformation(
                    "HIWU COFL v2 call {Path} was denied with status {StatusCode}. Trying v1.",
                    v2Path,
                    (int)v2.Status);
            else
                _logger.LogInformation("HIWU COFL v2 route {Path} is not available. Trying v1.", v2Path);
            var v1 = await PostAsync(v1Path, v1Payload, cancellationToken);
            return ToResult(v1, "v1");
        }

        private async Task<HiwuCoflCallResult> GetWithFallbackAsync(
            string v2Path,
            string v1Path,
            CancellationToken cancellationToken)
        {
            using var v2Request = await CreateRequestAsync(HttpMethod.Get, v2Path, cancellationToken);
            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var v2Response = await client.SendAsync(v2Request, cancellationToken);
            var v2Body = await v2Response.Content.ReadAsStringAsync(cancellationToken);
            if (v2Response.StatusCode is HttpStatusCode.OK or HttpStatusCode.Created)
                return ToResult((v2Response.StatusCode, v2Body), "v2");

            var v2Route = v2Path.Split('?')[0];
            if (IsMissingRoute(v2Response.StatusCode, v2Body))
                _logger.LogInformation("HIWU COFL v2 route {Path} is not available. Trying v1.", v2Route);
            else
                _logger.LogInformation(
                    "HIWU COFL v2 call {Path} failed with status {StatusCode}. Trying v1.",
                    v2Route,
                    (int)v2Response.StatusCode);
            using var v1Request = await CreateRequestAsync(HttpMethod.Get, v1Path, cancellationToken);
            using var v1Response = await client.SendAsync(v1Request, cancellationToken);
            var v1Body = await v1Response.Content.ReadAsStringAsync(cancellationToken);
            return ToResult((v1Response.StatusCode, v1Body), "v1");
        }

        private async Task<(HttpStatusCode Status, string Body)> PostAsync(
            string relativePath,
            object payload,
            CancellationToken cancellationToken)
        {
            await _postLock.WaitAsync(cancellationToken);
            try
            {
                var wait = _nextPostUtc - DateTime.UtcNow;
                if (wait > TimeSpan.Zero)
                    await Task.Delay(wait, cancellationToken);

                using var request = await CreateRequestAsync(HttpMethod.Post, relativePath, cancellationToken);
                request.Content = JsonContent.Create(payload, options: JsonOptions);
                var client = _httpClientFactory.CreateClient(HttpClientName);
                using var response = await client.SendAsync(request, cancellationToken);
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                _nextPostUtc = DateTime.UtcNow.AddMilliseconds(IsRateLimited(response.StatusCode, body) ? 5000 : 400);
                return (response.StatusCode, body);
            }
            finally
            {
                _postLock.Release();
            }
        }

        private HiwuCoflCallResult ToResult((HttpStatusCode Status, string Body) response, string apiVersion)
        {
            var succeeded = response.Status is HttpStatusCode.OK or HttpStatusCode.Created;
            var error = succeeded ? null : ReadError(response.Body, (int)response.Status);
            if (!succeeded)
            {
                _logger.LogWarning(
                    "HIWU COFL {ApiVersion} call failed with status {StatusCode}: {Error}",
                    apiVersion,
                    (int)response.Status,
                    error);
            }

            return new HiwuCoflCallResult
            {
                Succeeded = succeeded,
                StatusCode = (int)response.Status,
                ApiVersion = apiVersion,
                ResourceId = succeeded ? ReadResourceId(response.Body) : null,
                ErrorMessage = error,
                ResponseBody = response.Body
            };
        }

        private HiwuCoflReceivingResult ToReceivingResult(
            (HttpStatusCode Status, string Body) response,
            string apiVersion,
            bool specimensSent)
        {
            var call = ToResult(response, apiVersion);
            var animalIds = new List<long>();
            var specimenIds = new List<long>();
            if (call.Succeeded)
                ReadChildIds(response.Body, animalIds, specimenIds);

            return new HiwuCoflReceivingResult
            {
                Succeeded = call.Succeeded,
                StatusCode = call.StatusCode,
                ApiVersion = apiVersion,
                AccessionId = call.ResourceId,
                AnimalIds = animalIds,
                SpecimenIds = specimenIds,
                SpecimensSent = specimensSent && call.Succeeded,
                ErrorMessage = call.ErrorMessage
            };
        }

        private static void ReadChildIds(string body, List<long> animalIds, List<long> specimenIds)
        {
            if (string.IsNullOrWhiteSpace(body))
                return;

            try
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind != JsonValueKind.Object)
                    return;
                if (root.TryGetProperty("accession", out var accession) && accession.ValueKind == JsonValueKind.Object)
                    root = accession;

                CollectIds(root, "animals", animalIds, "specimens", specimenIds);
                CollectIds(root, "specimens", specimenIds, null, null);
            }
            catch (JsonException)
            {
            }
        }

        private static void CollectIds(
            JsonElement element,
            string name,
            List<long> ids,
            string? childName,
            List<long>? childIds)
        {
            if (!element.TryGetProperty(name, out var node))
                return;

            if (node.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in node.EnumerateArray())
                    Take(item);
                return;
            }

            if (node.ValueKind == JsonValueKind.Object)
                Take(node);

            void Take(JsonElement item)
            {
                if (item.ValueKind != JsonValueKind.Object)
                    return;
                if (TryReadLong(item, "id", out var id) && !ids.Contains(id))
                    ids.Add(id);
                if (childName != null && childIds != null)
                    CollectIds(item, childName, childIds, null, null);
            }
        }

        private static bool IsRateLimited(HttpStatusCode status, string? body) =>
            status == HttpStatusCode.TooManyRequests
            || (body != null && body.Contains("rate limit", StringComparison.OrdinalIgnoreCase));

        private static bool IsAccessDenied(HttpStatusCode status) =>
            status is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden;

        private static bool IsMissingRoute(HttpStatusCode status, string body)
        {
            if (status != HttpStatusCode.NotFound)
                return false;
            if (body.Contains("Couldn't find", StringComparison.OrdinalIgnoreCase)
                || body.Contains("Could not find", StringComparison.OrdinalIgnoreCase))
                return false;

            return body.Contains("doesn't exist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("does not exist", StringComparison.OrdinalIgnoreCase)
                || body.Contains("<html", StringComparison.OrdinalIgnoreCase);
        }

        private static long? ReadAnimalId(string body, long accessionId, string? tagNum)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                long? found = null;
                Walk(document.RootElement);
                return found;

                void Walk(JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray())
                            Walk(item);
                        return;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                        return;

                    var isSpecimen = element.TryGetProperty("specimen_type_id", out _)
                        || element.TryGetProperty("specimen_type", out _);
                    var isTag = element.TryGetProperty("tag_type_id", out _);
                    var isAnimal = !isTag && (
                        element.TryGetProperty("breed_id", out _)
                        || element.TryGetProperty("sex_code_id", out _)
                        || element.TryGetProperty("animal_tags", out _)
                        || element.TryGetProperty("animal_tags_attributes", out _));
                    if (!isSpecimen
                        && isAnimal
                        && TryReadId(element, out var id)
                        && id != accessionId
                        && id != 0
                        && (string.IsNullOrWhiteSpace(tagNum) || HasTagNum(element, tagNum))
                        && (found == null || id < found))
                        found = id;

                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            Walk(property.Value);
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static bool HasTagNum(JsonElement element, string tagNum)
        {
            foreach (var name in new[] { "animal_tags", "animal_tags_attributes" })
            {
                if (!element.TryGetProperty(name, out var tags) || tags.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (var tag in tags.EnumerateArray())
                {
                    if (tag.ValueKind != JsonValueKind.Object || !tag.TryGetProperty("num", out var num))
                        continue;
                    var value = num.ValueKind == JsonValueKind.String ? num.GetString() : num.ToString();
                    if (string.Equals(value?.Trim(), tagNum, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }

            return false;
        }

        private static bool IsAlreadyAssigned(string? message) =>
            message != null && (
                message.Contains("already", StringComparison.OrdinalIgnoreCase)
                || message.Contains("taken", StringComparison.OrdinalIgnoreCase)
                || message.Contains("duplicate", StringComparison.OrdinalIgnoreCase));

        private static long? ReadSpecimenId(string body, long animalId, long accessionId)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                long? found = null;
                Walk(document.RootElement);
                return found;

                void Walk(JsonElement element)
                {
                    if (element.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in element.EnumerateArray())
                            Walk(item);
                        return;
                    }

                    if (element.ValueKind != JsonValueKind.Object)
                        return;

                    var isSpecimen = element.TryGetProperty("specimen_type_id", out _)
                        || element.TryGetProperty("specimen_type", out _);
                    if (isSpecimen && TryReadId(element, out var id) && id != animalId && id != accessionId && id != 0)
                        found = id;

                    foreach (var property in element.EnumerateObject())
                    {
                        if (property.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array)
                            Walk(property.Value);
                    }
                }
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static long? ReadResourceId(string body)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                return FindId(document.RootElement);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? ReadAccessionNumber(string? body, long accessionId)
        {
            if (string.IsNullOrWhiteSpace(body))
                return null;

            try
            {
                using var document = JsonDocument.Parse(body);
                return FindAccessionNumber(document.RootElement, accessionId);
            }
            catch (JsonException)
            {
                return null;
            }
        }

        private static string? FindAccessionNumber(JsonElement element, long accessionId)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                if (TryReadLong(element, "id", out var id)
                    && id == accessionId
                    && element.TryGetProperty("num", out var num)
                    && num.ValueKind is JsonValueKind.String or JsonValueKind.Number)
                {
                    var value = num.ValueKind == JsonValueKind.String ? num.GetString()?.Trim() : num.GetRawText();
                    if (!string.IsNullOrEmpty(value) && value != accessionId.ToString())
                        return value;
                }

                foreach (var property in element.EnumerateObject())
                {
                    var nested = FindAccessionNumber(property.Value, accessionId);
                    if (nested != null)
                        return nested;
                }
            }
            else if (element.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in element.EnumerateArray())
                {
                    var nested = FindAccessionNumber(item, accessionId);
                    if (nested != null)
                        return nested;
                }
            }

            return null;
        }

        private static long? FindId(JsonElement element)
        {
            if (element.ValueKind == JsonValueKind.Object)
            {
                foreach (var name in new[] { "animal", "specimen", "data" })
                {
                    if (element.TryGetProperty(name, out var child) && child.ValueKind == JsonValueKind.Object)
                    {
                        var nested = FindId(child);
                        if (nested != null)
                            return nested;
                    }
                }

                if (TryReadLong(element, "id", out var id))
                    return id;
            }

            return null;
        }

        private static bool TryReadLong(JsonElement element, string name, out long value)
        {
            value = 0;
            if (!element.TryGetProperty(name, out var property))
                return false;
            if (property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out value))
                return true;
            return property.ValueKind == JsonValueKind.String && long.TryParse(property.GetString(), out value);
        }

        private static string ReadError(string body, int statusCode)
        {
            if (!string.IsNullOrWhiteSpace(body) && !body.Contains("<html", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    using var document = JsonDocument.Parse(body);
                    var root = document.RootElement;
                    if (root.ValueKind == JsonValueKind.Object)
                    {
                        if (root.TryGetProperty("error", out var error))
                            return Trim(error.ValueKind == JsonValueKind.String ? error.GetString() ?? error.ToString() : error.ToString());
                        if (root.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
                            return Trim(message.GetString() ?? "");
                    }
                }
                catch (JsonException)
                {
                    return Trim(body);
                }

                return Trim(body);
            }

            return "TraceFirst returned status " + statusCode + ".";
        }

        private static string Trim(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= 500)
                return value;
            return value[..500];
        }

        private async Task<HiwuCoflSession> LoginAsync(CancellationToken cancellationToken)
        {
            var cofl = _options.Value.Cofl;
            var missing = HiwuCoflSettingsReader.GetMissingFields(cofl);
            if (missing.Count > 0)
            {
                throw new InvalidOperationException(
                    "HIWU COFL settings are incomplete (" + string.Join(", ", missing) +
                    "). Expected Parameter Store " + HiwuCoflCredentialPaths.ConfigurationPath + ".");
            }

            var client = _httpClientFactory.CreateClient(HttpClientName);
            using var v2Response = await SendLoginAsync(client, cofl, "v2", cancellationToken);
            if (v2Response.StatusCode != System.Net.HttpStatusCode.NotFound)
                return await ReadSessionAsync(v2Response, cofl, "v2", cancellationToken);

            _logger.LogInformation("HIWU COFL v2 login is not available. Trying v1.");
            using var v1Response = await SendLoginAsync(client, cofl, "v1", cancellationToken);
            return await ReadSessionAsync(v1Response, cofl, "v1", cancellationToken);
        }

        private static async Task<HttpResponseMessage> SendLoginAsync(
            HttpClient client,
            HiwuCoflOptions cofl,
            string apiVersion,
            CancellationToken cancellationToken)
        {
            var loginUri = BuildLoginUri(cofl.Website, cofl.Username, cofl.Password, apiVersion);
            using var request = new HttpRequestMessage(HttpMethod.Get, loginUri);
            return await client.SendAsync(request, cancellationToken);
        }

        private async Task<HiwuCoflSession> ReadSessionAsync(
            HttpResponseMessage response,
            HiwuCoflOptions cofl,
            string apiVersion,
            CancellationToken cancellationToken)
        {
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
                    "HIWU COFL {ApiVersion} login failed with status {StatusCode}.",
                    apiVersion,
                    (int)response.StatusCode);
                throw new InvalidOperationException(
                    "HIWU COFL " + apiVersion + " login failed with status " + (int)response.StatusCode + ".");
            }

            var body = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken);
            if (body == null || string.IsNullOrWhiteSpace(body.AuthToken))
                throw new InvalidOperationException("HIWU COFL " + apiVersion + " login response did not include auth_token.");

            if (body.Suspended)
                throw new InvalidOperationException("HIWU COFL " + apiVersion + " login was rejected because the account is suspended.");

            if (body.AuthTokenExpiresAt == null)
                throw new InvalidOperationException("HIWU COFL " + apiVersion + " login response did not include auth_token_expires_at.");

            var session = new HiwuCoflSession
            {
                Email = string.IsNullOrWhiteSpace(body.Email) ? cofl.Username : body.Email,
                Token = body.AuthToken,
                ExpiresAt = body.AuthTokenExpiresAt.Value,
                UserId = body.Id
            };

            _logger.LogInformation(
                "HIWU COFL {ApiVersion} login succeeded. Token expires at {ExpiresAt}.",
                apiVersion,
                session.ExpiresAt);
            return session;
        }

        private static bool IsCurrent(HiwuCoflSession? session) =>
            session != null
            && !string.IsNullOrWhiteSpace(session.Token)
            && session.ExpiresAt > DateTimeOffset.UtcNow.Add(RefreshBuffer);

        private static Uri BuildLoginUri(string website, string email, string password, string apiVersion)
        {
            var builder = new UriBuilder(RequireAbsoluteWebsite(website));
            var path = builder.Path.TrimEnd('/');
            builder.Path = path + "/api/" + apiVersion + "/access_control/login";
            builder.Query = "email=" + Uri.EscapeDataString(email) + "&password=" + Uri.EscapeDataString(password);
            return builder.Uri;
        }

        private static Uri BuildApiUri(string website, string relativePath)
        {
            var split = relativePath.Trim().Split('?', 2);
            var builder = new UriBuilder(RequireAbsoluteWebsite(website));
            var path = builder.Path.TrimEnd('/');
            var relative = split[0].TrimStart('/');
            builder.Path = string.IsNullOrEmpty(relative) ? path : path + "/" + relative;
            builder.Query = split.Length == 2 ? split[1] : "";
            return builder.Uri;
        }

        private static Uri RequireAbsoluteWebsite(string website)
        {
            if (!Uri.TryCreate(website.Trim(), UriKind.Absolute, out var uri)
                || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
            {
                throw new InvalidOperationException(
                    "HIWU COFL website is not an absolute http or https URL. Expected Parameter Store "
                    + HiwuCoflCredentialPaths.ConfigurationPath + ".");
            }

            return uri;
        }

        private sealed class LoginResponse
        {
            [JsonPropertyName("email")]
            public string? Email { get; set; }

            [JsonPropertyName("auth_token")]
            public string? AuthToken { get; set; }

            [JsonPropertyName("auth_token_expires_at")]
            public DateTimeOffset? AuthTokenExpiresAt { get; set; }

            [JsonPropertyName("suspended")]
            public bool Suspended { get; set; }

            [JsonPropertyName("id")]
            public long Id { get; set; }
        }

        private sealed class AccessionEnvelope(AccessionBody accession)
        {
            public AccessionBody Accession { get; } = accession;
        }

        private sealed class AccessionBody
        {
            public AccessionBody(HiwuCoflAccessionRequest request)
            {
                LabId = request.LabId;
                ClientId = request.ClientId;
                UserId = request.UserId;
                VetId = request.VetId;
                CarrierId = request.CarrierId;
                CarrierRefNum = request.CarrierRefNum;
                OwnerId = request.OwnerId;
                AdditionalInfo = request.AdditionalInfo;
                PreAccessionRef = request.PreAccessionRef;
                Priority = request.Priority;
            }

            public long LabId { get; }
            public long ClientId { get; }
            public long? UserId { get; }
            public long? VetId { get; }
            public long? CarrierId { get; }
            public string? CarrierRefNum { get; }
            public long? OwnerId { get; }
            public string? AdditionalInfo { get; }
            public string? PreAccessionRef { get; }
            public int? Priority { get; }
        }

        private sealed class AnimalEnvelope(AnimalBody animal)
        {
            public AnimalBody Animal { get; } = animal;
        }

        private sealed class AnimalBody
        {
            public AnimalBody(HiwuCoflAnimalRequest request, bool tagsAsAttributes)
            {
                AccessionId = request.AccessionId;
                BreedId = request.BreedId;
                SexCodeId = request.SexCodeId;
                WeightUomId = request.WeightUomId;
                AgeUomId = request.AgeUomId;
                Weight = request.Weight;
                Age = request.Age;
                Legal = request.Legal;
                Insured = request.Insured;
                Name = request.Name;
                NonAnimal = request.NonAnimal;
                var tags = request.Tags.Count == 0
                    ? null
                    : request.Tags.Select(tag => new AnimalTagBody(tag.TagTypeId, tag.Num)).ToList();
                if (tagsAsAttributes)
                    AnimalTagsAttributes = tags;
                else
                    AnimalTags = tags;
                if (tags != null)
                    Name = null;
            }

            public long AccessionId { get; }
            public long? BreedId { get; }
            public long? SexCodeId { get; }
            public long? WeightUomId { get; }
            public long? AgeUomId { get; }
            public string? Weight { get; }
            public string? Age { get; }
            public bool? Legal { get; }
            public bool? Insured { get; }
            public string? Name { get; }
            public bool? NonAnimal { get; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<AnimalTagBody>? AnimalTags { get; }

            [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
            public List<AnimalTagBody>? AnimalTagsAttributes { get; }
        }

        private sealed class AnimalTagBody(long tagTypeId, string num)
        {
            public long TagTypeId { get; } = tagTypeId;
            public string Num { get; } = num;
        }

        private sealed class SpecimenEnvelope(SpecimenBody specimen)
        {
            public SpecimenBody Specimen { get; } = specimen;
        }

        private sealed class SpecimenBody
        {
            public SpecimenBody(HiwuCoflSpecimenRequest request)
            {
                AnimalId = request.AnimalId;
                SpecimenTypeId = request.SpecimenTypeId;
                SpecimenTypeDescriptionId = request.SpecimenTypeDescriptionId;
                CollectionDate = request.CollectionDate;
                TransportMediumId = request.TransportMediumId;
                ConditionId = request.ConditionId;
                LocationId = request.LocationId;
                Name = request.Name;
                OriginalSpecimenId = request.OriginalSpecimenId;
                CaseCoordinatorComment = request.CaseCoordinatorComment;
            }

            public long AnimalId { get; }
            public long? SpecimenTypeId { get; }
            public long? SpecimenTypeDescriptionId { get; }
            public string? CollectionDate { get; }
            public long? TransportMediumId { get; }
            public long? ConditionId { get; }
            public long? LocationId { get; }
            public string? Name { get; }
            public long? OriginalSpecimenId { get; }
            public string? CaseCoordinatorComment { get; }
        }

        private sealed class TestOrderBody
        {
            public TestOrderBody(long specimenId, IReadOnlyList<long> testScheduleIds)
            {
                SpecimenTestSchedules = testScheduleIds
                    .Select(id => new SpecimenTestScheduleBody(specimenId, id))
                    .ToList();
            }

            public List<SpecimenTestScheduleBody> SpecimenTestSchedules { get; }
        }

        private sealed class SpecimenTestScheduleBody(long specimenId, long testScheduleId)
        {
            public long SpecimenId { get; } = specimenId;
            public long TestScheduleId { get; } = testScheduleId;
        }

        private sealed class ReceivingEnvelope<T>(T accession)
        {
            public T Accession { get; } = accession;
        }

        private sealed class ReceivingV2Accession
        {
            public ReceivingV2Accession(HiwuCoflReceivingRequest request)
            {
                LabId = request.LabId;
                ClientId = request.ClientId;
                UserId = request.UserId;
                OwnerId = request.OwnerId;
                Priority = request.Priority;
                PreAccessionRef = request.PreAccessionRef;
                CarrierId = request.CarrierId;
                CarrierRefNum = request.CarrierRefNum;
                AdditionalInfo = request.AdditionalInfo;
                AnimalsAttributes = request.Animals.Count == 0
                    ? null
                    : request.Animals.Select(animal => new ReceivingV2Animal(animal)).ToList();
            }

            public long LabId { get; }
            public long ClientId { get; }
            public long? UserId { get; }
            public long? OwnerId { get; }
            public int? Priority { get; }
            public string? PreAccessionRef { get; }
            public long? CarrierId { get; }
            public string? CarrierRefNum { get; }
            public string? AdditionalInfo { get; }
            public List<ReceivingV2Animal>? AnimalsAttributes { get; }
        }

        private sealed class ReceivingV2Animal
        {
            public ReceivingV2Animal(HiwuCoflReceivingAnimal animal)
            {
                BreedId = animal.BreedId;
                SexCodeId = animal.SexCodeId;
                Weight = animal.Weight;
                Age = animal.Age;
                AgeUomId = animal.AgeUomId;
                Legal = animal.Legal;
                Insured = animal.Insured;
                Name = animal.Name;
                NonAnimal = animal.NonAnimal;
                AnimalTagsAttributes = animal.Tags.Count == 0
                    ? null
                    : animal.Tags.Select(tag => new AnimalTagBody(tag.TagTypeId, tag.Num)).ToList();
                SpecimensAttributes = animal.Specimens.Count == 0
                    ? null
                    : animal.Specimens.Select(specimen => new ReceivingSpecimen(specimen)).ToList();
            }

            public long? BreedId { get; }
            public long? SexCodeId { get; }
            public string? Weight { get; }
            public string? Age { get; }
            public long? AgeUomId { get; }
            public bool? Legal { get; }
            public bool? Insured { get; }
            public string? Name { get; }
            public bool? NonAnimal { get; }
            public List<AnimalTagBody>? AnimalTagsAttributes { get; }
            public List<ReceivingSpecimen>? SpecimensAttributes { get; }
        }

        private sealed class ReceivingSpecimen
        {
            public ReceivingSpecimen(HiwuCoflReceivingSpecimen specimen)
            {
                SpecimenTypeId = specimen.SpecimenTypeId;
                SpecimenTypeDescriptionId = specimen.SpecimenTypeDescriptionId;
                CollectionDate = specimen.CollectionDate;
                TransportMediumId = specimen.TransportMediumId;
                ConditionId = specimen.ConditionId;
                LocationId = specimen.LocationId;
                Name = specimen.Name;
                OriginalSpecimenId = specimen.OriginalSpecimenId;
                CaseCoordinatorComment = specimen.CaseCoordinatorComment;
            }

            public long? SpecimenTypeId { get; }
            public long? SpecimenTypeDescriptionId { get; }
            public string? CollectionDate { get; }
            public long? TransportMediumId { get; }
            public long? ConditionId { get; }
            public long? LocationId { get; }
            public string? Name { get; }
            public long? OriginalSpecimenId { get; }
            public string? CaseCoordinatorComment { get; }
        }

        private sealed class ReceivingV1Accession
        {
            public ReceivingV1Accession(HiwuCoflReceivingRequest request)
            {
                LabId = request.LabId;
                ClientId = request.ClientId;
                UserId = request.UserId;
                OwnerId = request.OwnerId;
                Priority = request.Priority;
                PreAccessionRef = request.PreAccessionRef;
                AnimalAttributes = request.Animals.Count == 0
                    ? null
                    : request.Animals.Select(animal => new ReceivingV1Animal(animal)).ToList();
            }

            public long LabId { get; }
            public long ClientId { get; }
            public long? UserId { get; }
            public long? OwnerId { get; }
            public int? Priority { get; }
            public string? PreAccessionRef { get; }
            public List<ReceivingV1Animal>? AnimalAttributes { get; }
        }

        private sealed class ReceivingV1Animal
        {
            public ReceivingV1Animal(HiwuCoflReceivingAnimal animal)
            {
                BreedId = animal.BreedId;
                SexCodeId = animal.SexCodeId;
                WeightUomId = animal.WeightUomId;
                AgeUomId = animal.AgeUomId;
                Weight = animal.Weight;
                Age = animal.Age;
                Legal = animal.Legal;
                Insured = animal.Insured;
                Name = animal.Name;
                AnimalTagAttributes = animal.Tags.Count == 0
                    ? null
                    : animal.Tags.Select(tag => new AnimalTagBody(tag.TagTypeId, tag.Num)).ToList();
            }

            public long? BreedId { get; }
            public long? SexCodeId { get; }
            public long? WeightUomId { get; }
            public long? AgeUomId { get; }
            public string? Weight { get; }
            public string? Age { get; }
            public bool? Legal { get; }
            public bool? Insured { get; }
            public string? Name { get; }
            public List<AnimalTagBody>? AnimalTagAttributes { get; }
        }

        private sealed class TestOrderV1Body
        {
            public TestOrderV1Body(long specimenId, IReadOnlyList<long> testScheduleIds)
            {
                SpecimenId = specimenId;
                TestScheduleIds = string.Join(',', testScheduleIds);
            }

            public long SpecimenId { get; }
            public string TestScheduleIds { get; }
        }
    }
}
