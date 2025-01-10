using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

public sealed record UserNormalizationResult
{
    public required string Username { get; init; }
    public required string Name { get; init; }
    public required bool IsInternal { get; init; }
}

public sealed class RunNewUserWorkFlow
{
    public static async Task ExecuteWorkflowForUsersAsync(List<UserNormalizationResult> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (users.Count == 0)
        {
            return;
        }

        try
        {
            var serviceClient = SessionManager.Instance.GetClient();

            // Query for the workflow
            var workflow = GetNewUserWorkflow(serviceClient);
            if (workflow is null)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("Workflow not found or not in a published state.");
                Console.ResetColor();
                WaitForUserInput();
                return;
            }

            var user = users[0];  // Process the first user
            try
            {
                await ProcessUserWorkflowAsync(serviceClient, workflow, user);
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine($"Error processing user {user.Username}: {ex.Message}");
                Console.ResetColor();
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"An error occurred: {ex.Message}");
            Console.ResetColor();
        }

        WaitForUserInput();
    }

    private static void WaitForUserInput()
    {
        Console.WriteLine("\nPress Enter to continue...");
        Console.ReadLine();
    }

    private static Entity? GetNewUserWorkflow(ServiceClient serviceClient)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);

        var query = new QueryExpression("workflow")
        {
            ColumnSet = new ColumnSet("workflowid", "name", "statecode", "statuscode"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, CodesAndRoles.NewUserWorkflow),
                    new ConditionExpression("type", ConditionOperator.Equal, 1),
                    new ConditionExpression("category", ConditionOperator.Equal, 0),
                    new ConditionExpression("statecode", ConditionOperator.Equal, 1)
                }
            }
        };

        return serviceClient.RetrieveMultiple(query).Entities.FirstOrDefault();
    }

    private static async Task ProcessUserWorkflowAsync(ServiceClient serviceClient, Entity workflow, UserNormalizationResult user)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);
        ArgumentNullException.ThrowIfNull(workflow);
        ArgumentNullException.ThrowIfNull(user);

        // Get the system user
        var systemUser = GetSystemUser(serviceClient, user.Username);
        if (systemUser is null)
        {
            Console.WriteLine($"System user not found for {user.Username}");
            throw new InvalidOperationException($"System user not found for {user.Username}");
        }

        var executeWorkflowRequest = new OrganizationRequest("ExecuteWorkflow")
        {
            ["WorkflowId"] = workflow.Id,
            ["EntityId"] = systemUser.Id
        };

        await Task.Run(() => ExecuteWorkflowWithRetry(serviceClient, executeWorkflowRequest, user));
    }

    private static void ExecuteWorkflowWithRetry(
        ServiceClient serviceClient,
        OrganizationRequest request,
        UserNormalizationResult user)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(user);

        try
        {
            serviceClient.Execute(request);
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"Workflow executed successfully for new user: {user.Name}, Username: {user.Username}");
            Console.ResetColor();
        }
        catch (Exception ex)
        {
            if (IsResourceExistsException(ex))
            {
                Console.ForegroundColor = ConsoleColor.Blue;
                Console.WriteLine("\nRunning workflow to activate user");
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine($"User {user.Username} is already activated (Resource record exists)");
                Console.ResetColor();
                return;
            }

            throw;
        }
    }

    private static bool IsResourceExistsException(Exception ex)
    {
        var message = ex.Message.ToLowerInvariant();
        return message.Contains("ya hay otro registro de recurso asociado a este usuario") ||
               message.Contains("there is already another resource record associated to this user") ||
               message.Contains("ya existe un registro creado con el nombre seleccionado") ||
               message.Contains("a record was not created or updated because a duplicate of the current record already exists");
    }

    private static Entity? GetSystemUser(ServiceClient serviceClient, string username)
    {
        ArgumentNullException.ThrowIfNull(serviceClient);
        ArgumentNullException.ThrowIfNull(username);

        try
        {
            var query = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "domainname"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("domainname", ConditionOperator.Like, $"%{username}%")
                    }
                }
            };

            return serviceClient.RetrieveMultiple(query).Entities.FirstOrDefault();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error retrieving system user: {ex.Message}");
            return null;
        }
    }
}