using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Text.RegularExpressions;
using System.Xml.Linq;


    public sealed class WorkOrderViewCreator
    {
        private readonly List<TransformedTeamData> _teamDataList;

        public WorkOrderViewCreator(List<TransformedTeamData> teamDataList)
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
            string fetchXml = BuildWorkOrderQuery();  

            Console.Clear();
            Console.WriteLine("Generated FetchXML Query for Work Orders:");
            Console.WriteLine(fetchXml.Replace("><", ">\n<")); 

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

            var serviceClient = SessionManager.Instance.GetClient();
            var viewId = await CreatePersonalViewAsync(serviceClient, fetchXml, viewName, cts.Token);

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"\nView '{viewName}' created successfully with ID: {viewId}");
            Console.ResetColor();
            Console.WriteLine("\nPress Enter to continue");
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

            return new ViewCreationResult
            {
                ViewName = "Error",
                Success = false,
                ViewId = null,
                ErrorMessage = ex.Message
            };
        }
    }

    private string BuildWorkOrderQuery()
        {
            // Create the base XDocument with common attributes
            XDocument doc = new XDocument(
                new XElement("fetch",
                    new XElement("entity",
                        new XAttribute("name", "msdyn_workorder"),
                        CreateAttributeElements(),
                        CreateOrderElement(),
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
                "statecode", "msdyn_name", "msdyn_serviceaccount", "atos_codigoordentrabajosap",
                "atos_titulo", "statuscode", "atos_puestotrabajoprincipalid", "atos_grupoplanificadorid",
                "atos_estadousuario", "atos_fechadecreacinensap", "createdon", "atos_inicextr",
                "atos_fechainicioprogramado", "atos_finextr", "atos_fechafinprogramado", "msdyn_workorderid"
            };

            return attributes.Select(attr => new XElement("attribute", new XAttribute("name", attr)));
        }

        private XElement CreateOrderElement()
        {
            return new XElement("order",
                new XAttribute("attribute", "createdon"),
                new XAttribute("descending", "true"));
        }

        private XElement CreateBaseFilter()
        {
            return new XElement("condition",
                new XAttribute("attribute", "statuscode"),
                new XAttribute("operator", "ne"),
                new XAttribute("value", "300000005"));
        }

        private XElement CreateOwnerFilter()
        {
            var truncatedUniqueContractorCodes = _teamDataList
                .Select(t => t.ContractorCode)
                .Select(code => code.Length >= 4 ? code[..4] : code)
                .Distinct();

            return new XElement("filter",
                new XAttribute("type", "and"),
                truncatedUniqueContractorCodes.Select(code =>
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
                            new XAttribute("attribute", "msdyn_serviceaccountname"),
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

        Console.Write("\nEnter a name for the new Work Order view (or press Enter to cancel): ");  // Fixed: Changed Notifications to Work Order
        viewName = Console.ReadLine()?.Trim();

        if (string.IsNullOrWhiteSpace(viewName))
        {
            Console.WriteLine("View creation cancelled.");
            Console.WriteLine("\nPress Enter to return to main menu...");
            Console.ReadKey(true);
            Console.ResetColor();
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
                ["returnedtypecode"] = "msdyn_workorder",
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
                <grid name='resultset' object='10010' jump='name' select='1' icon='1' preview='1'>
                  <row name='result' id='msdyn_workorderid'>
                    <cell name='msdyn_name' width='300' />
                    <cell name='msdyn_serviceaccount' width='150' />
                    <cell name='atos_codigoordentrabajosap' width='100' />
                    <cell name='atos_titulo' width='200' />
                    <cell name='statuscode' width='100' />
                    <cell name='atos_puestotrabajoprincipalid' width='150' />
                    <cell name='atos_grupoplanificadorid' width='150' />
                    <cell name='atos_estadousuario' width='100' />
                    <cell name='createdon' width='125' />
                  </row>
                </grid>
                """;
        }
    }
