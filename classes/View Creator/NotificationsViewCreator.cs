using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Text.RegularExpressions;
using System.Xml.Linq;

    public sealed class NotificationsViewCreator
    {
        private readonly List<TransformedTeamData> _teamDataList;

        public NotificationsViewCreator(List<TransformedTeamData> teamDataList)
        {
            _teamDataList = teamDataList ?? throw new ArgumentNullException(nameof(teamDataList));
        }

        public sealed record ViewCreationResult
        {
            public required string ViewName { get; init; }
            public required bool Success { get; init; }
            public required Guid? ViewId { get; init; }
            public bool Cancelled { get; init; }
            public string? ErrorMessage { get; init; }
        }

    public async Task<ViewCreationResult> RunAsync()
    {
        try
        {

            using var cts = new CancellationTokenSource();
            string fetchXml = BuildNotificationsQuery();

            Console.Clear();
            Console.WriteLine("Generated FetchXML Query for Notifications:");
            Console.WriteLine(fetchXml.Replace("><", ">\n<")); // Better formatting

            var viewName = await PromptForViewNameAsync(cts.Token);
            if (string.IsNullOrEmpty(viewName))
            {
                return new ViewCreationResult
                {
                    ViewName = "Cancelled",
                    Success = false,
                    ViewId = null,
                    Cancelled = true
                };
            }

            using var serviceClient = SessionManager.Instance.GetClient();
            var viewId = await CreatePersonalViewAsync(serviceClient, fetchXml, viewName, cts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nView '{viewName}' created successfully with ID: {viewId}");
            Console.ResetColor();
            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadKey(true);

            return new ViewCreationResult
            {
                ViewName = viewName,
                Success = true,
                ViewId = viewId,
                Cancelled = false
            };
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"\nError: {ex.Message}");
            Console.ResetColor();
            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadKey(true);
            Console.ResetColor();

            return new ViewCreationResult
            {
                ViewName = "Error",
                Success = false,
                ViewId = null,
                ErrorMessage = ex.Message
            };
        }
    }

    private string BuildNotificationsQuery()
        {
            XDocument doc = new XDocument(
                new XElement("fetch",
                    new XElement("entity",
                        new XAttribute("name", "atos_aviso"),
                        CreateAttributeElements(),
                        CreateOrderElements(),
                        new XElement("filter",
                            new XAttribute("type", "and"),
                            CreateBaseFilter(),
                            CreateOwnerFilter(),
                            CreatePlannerGroupFilter(),
                            CreateContractorFilter(),
                            CreateBusinessUnitFilter()
                        )
                    )
                )
            );

            return doc.ToString(SaveOptions.None);
        }

        private IEnumerable<XElement> CreateAttributeElements()
        {
            string[] attributes = new[]
            {
                "statecode", "atos_name", "atos_ubicaciontecnicaid", "statuscode",
                "atos_fechainicioaveria", "atos_fechanotificacion", "atos_prioridadid",
                "atos_esprincipal", "atos_fechafinaveria", "atos_ordendetrabajoid",
                "atos_equipoid", "atos_clasedeavisoid", "atos_esplantilla",
                "atos_codigosap", "atos_descripcioncorta", "atos_avisoid"
            };

            return attributes.Select(attr => new XElement("attribute", new XAttribute("name", attr)));
        }

        private IEnumerable<XElement> CreateOrderElements()
        {
            yield return new XElement("order",
                new XAttribute("attribute", "atos_name"),
                new XAttribute("descending", "true"));

            yield return new XElement("order",
                new XAttribute("attribute", "atos_clasedeavisoid"),
                new XAttribute("descending", "false"));
        }

        private XElement CreateBaseFilter()
        {
            return new XElement("condition",
                new XAttribute("attribute", "atos_indicadorborrado"),
                new XAttribute("operator", "ne"),
                new XAttribute("value", "1"));
        }

        private XElement CreateOwnerFilter()
        {
            var contractorCodes = _teamDataList
                .Select(t => t.ContractorCode)
                .Select(code => code.Length >= 4 ? code[..4] : code)
                .Distinct();

            return new XElement("filter",
                new XAttribute("type", "and"),
                contractorCodes.Select(code =>
                    new XElement("condition",
                        new XAttribute("attribute", "owneridname"),
                        new XAttribute("operator", "not-like"),
                        new XAttribute("value", $"%{code}%"))));
        }

        private XElement CreatePlannerGroupFilter()
        {
            return new XElement("filter",
                new XAttribute("type", "or"),
                _teamDataList.Select(t => t.PlannerGroup)
                    .Distinct()
                    .Select(group =>
                        new XElement("condition",
                            new XAttribute("attribute", "atos_grupoplanificadoridname"),
                            new XAttribute("operator", "like"),
                            new XAttribute("value", $"%{group}%"))));
        }

        private XElement CreateContractorFilter()
        {
            var contractors = _teamDataList
                .SelectMany(t => t.Contractor.Split(' '))
                .Distinct();

            return new XElement("filter",
                new XAttribute("type", "or"),
                contractors.Select(contractor =>
                    new XElement("condition",
                        new XAttribute("attribute", "atos_puestotrabajoprincipalidname"),
                        new XAttribute("operator", "like"),
                        new XAttribute("value", $"%{contractor}%"))));
        }

        private XElement CreateBusinessUnitFilter()
        {
            return new XElement("filter",
                new XAttribute("type", "or"),
                _teamDataList.Select(t => ExtractBuCode(t.Bu))
                    .Distinct()
                    .Select(bu =>
                        new XElement("condition",
                            new XAttribute("attribute", "atos_ubicaciontecnicaidname"),
                            new XAttribute("operator", "like"),
                            new XAttribute("value", $"%{bu}%"))));
        }

        private string ExtractBuCode(string input)
        {
            var regex = new Regex(@"^\d-[A-Z]{2}-[A-Z]{3}-\d{2}");
            var match = regex.Match(input);
            return match.Success ? match.Value : input;
        }

        private async Task<string?> PromptForViewNameAsync(CancellationToken cancellationToken)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            string? viewName = null;

            Console.Write("\nEnter a name for the new Notifications view (or press Enter to cancel): ");
            viewName = Console.ReadLine()?.Trim();

            if (string.IsNullOrWhiteSpace(viewName))
            {
                Console.WriteLine("View creation cancelled.");
                Console.WriteLine("\nPress Enter to return to main menu...");
                Console.ReadKey(true);
                return null;
            }

            Console.ResetColor();
            return viewName;
        }

        private async Task<Guid> CreatePersonalViewAsync(
                ServiceClient serviceClient,
                string fetchXml,
                string viewName,
                CancellationToken cancellationToken)
            {
                var userQuery = new Entity("userquery")
                {
                    ["returnedtypecode"] = "atos_aviso",
                    ["name"] = viewName,
                    ["fetchxml"] = fetchXml,
                    ["layoutxml"] = CreateLayoutXml(),
                    ["querytype"] = 0
                };

                return await Task.Run(() => serviceClient.Create(userQuery), cancellationToken);
            }

        private string CreateLayoutXml()
        {
            return """
                <grid name='resultset' object='10010' jump='atos_name' select='1' icon='1' preview='1'>
                  <row name='result' id='atos_avisoid'>
                    <cell name='atos_name' width='300' />
                    <cell name='atos_ubicaciontecnicaid' width='150' />
                    <cell name='statuscode' width='100' />
                    <cell name='atos_fechainicioaveria' width='125' />
                    <cell name='atos_fechanotificacion' width='125' />
                    <cell name='atos_prioridadid' width='100' />
                    <cell name='atos_esprincipal' width='100' />
                    <cell name='atos_ordendetrabajoid' width='150' />
                    <cell name='atos_equipoid' width='150' />
                    <cell name='atos_clasedeavisoid' width='150' />
                  </row>
                </grid>
                """;
        }
    }
