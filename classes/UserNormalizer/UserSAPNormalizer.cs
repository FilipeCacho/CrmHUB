using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System.Collections.Concurrent;

public static class UserSAPNormalizer
{
    public static async Task ProcessUsersAsync(List<UserNormalizationResult> users)
    {
        ArgumentNullException.ThrowIfNull(users);

        if (!users.Any())
        {
            return;
        }

        Console.ForegroundColor = ConsoleColor.Blue;
        Console.WriteLine("\nProcessing SAP Credentials for Users");
        Console.ResetColor();

        try
        {
            // Get the service client without using statement
            var serviceClient = SessionManager.Instance.GetClient();
            foreach (var user in users)
            {
                await ProcessUserAsync(user, serviceClient);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error in user processing: {ex.Message}");
            Console.ResetColor();
            throw;
        }
    }

    private static async Task ProcessUserAsync(UserNormalizationResult user, ServiceClient serviceClient)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentNullException.ThrowIfNull(serviceClient);

        if (!user.IsInternal)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"User {user.Username} is not internal. Skipping process.");
            Console.ResetColor();
            return;
        }

        var systemUser = await RetrieveSystemUserAsync(user.Username, serviceClient);
        if (systemUser is null)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"System user not found for {user.Username}. Skipping processing.");
            Console.ResetColor();
            return;
        }

        if (systemUser.GetAttributeValue<bool>("isdisabled"))
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"User {user.Username} is disabled. Skipping processing.");
            Console.ResetColor();
            return;
        }

        var sapUserId = systemUser.GetAttributeValue<EntityReference>("atos_usuariosapid");
        if (sapUserId is not null)
        {
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine($"User {user.Username} already has a SAP user assigned to it. Skipping processing.");
            Console.ResetColor();
            return;
        }

        var existingAtosUsuarios = await RetrieveAtosUsuariosAsync(user.Username, serviceClient);

        if (existingAtosUsuarios is not null)
        {
            await LinkExistingAtosUsuariosAsync(systemUser, existingAtosUsuarios, serviceClient);
        }
        else
        {
            await CreateAndLinkAtosUsuariosAsync(user, systemUser, serviceClient);
        }
    }

    private static async Task<Entity?> RetrieveSystemUserAsync(string username, ServiceClient serviceClient)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "isdisabled", "atos_usuariosapid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("domainname", ConditionOperator.Like, $"{username}%")
                }
            }
        };

        var result = await Task.Run(() => serviceClient.RetrieveMultiple(query));
        return result.Entities.FirstOrDefault();
    }

    private static async Task<Entity?> RetrieveAtosUsuariosAsync(string username, ServiceClient serviceClient)
    {
        var query = new QueryExpression("atos_usuarios")
        {
            ColumnSet = new ColumnSet("atos_usuariosid", "atos_name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("atos_codigo", ConditionOperator.Equal, username)
                }
            }
        };

        var result = await Task.Run(() => serviceClient.RetrieveMultiple(query));
        return result.Entities.FirstOrDefault();
    }

    private static async Task LinkExistingAtosUsuariosAsync(Entity systemUser, Entity atosUsuarios, ServiceClient serviceClient)
    {
        var systemUserUpdate = new Entity("systemuser")
        {
            Id = systemUser.Id,
            ["atos_usuariosapid"] = new EntityReference("atos_usuarios", atosUsuarios.Id)
        };

        await Task.Run(() => serviceClient.Update(systemUserUpdate));

        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine("Successfully updated user's SAP credentials");
        Console.ResetColor();
    }

    private static async Task CreateAndLinkAtosUsuariosAsync(UserNormalizationResult user, Entity systemUser, ServiceClient serviceClient)
    {
        var nameParts = user.Name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var firstName = nameParts[0];
        var lastName = string.Join(" ", nameParts.Skip(1));

        var atosUsuarios = new Entity("atos_usuarios")
        {
            ["atos_apellidos"] = lastName,
            ["atos_codigo"] = user.Username,
            ["atos_name"] = $"{user.Username}: {firstName} {lastName}",
            ["atos_nombre"] = firstName,
            ["atos_fechafin"] = new DateTime(9999, 12, 31)
        };

        try
        {
            var newRecordId = await Task.Run(() => serviceClient.Create(atosUsuarios));
            Console.WriteLine($"Created new atos_usuarios record with ID: {newRecordId}");

            var systemUserUpdate = new Entity("systemuser")
            {
                Id = systemUser.Id,
                ["atos_usuariosapid"] = new EntityReference("atos_usuarios", newRecordId)
            };

            await Task.Run(() => serviceClient.Update(systemUserUpdate));

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("Successfully updated user's SAP credentials");
            Console.ResetColor();
        }
        catch (Exception ex) when (IsDuplicateException(ex))
        {
            // If creation fails due to duplicate, try to retrieve and link the existing record
            var existingAtosUsuarios = await RetrieveAtosUsuariosAsync(user.Username, serviceClient);
            if (existingAtosUsuarios is not null)
            {
                await LinkExistingAtosUsuariosAsync(systemUser, existingAtosUsuarios, serviceClient);
            }
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error creating or linking atos_usuarios for {user.Username}: {ex.Message}");
            Console.ResetColor();
        }
    }

    private static bool IsDuplicateException(Exception ex)
    {
        return ex.Message.Contains("duplicate") ||
               ex.Message.Contains("already exists") ||
               ex.Message.Contains("unique constraint");
    }
}