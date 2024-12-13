using Microsoft.PowerPlatform.Dataverse.Client;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using System.Windows.Forms;
using System.Collections.Concurrent;

public class BulkRoleAssignment : IDisposable
{
    private readonly ServiceClient _serviceClient;
    private readonly Dictionary<(string roleName, Guid businessUnitId), Entity> _roleCache = new();
    private readonly CancellationTokenSource _cts;
    private readonly CancellationTokenSource _monitorCts;
    private readonly Task _monitorTask;

    public sealed record RoleAssignmentResult
    {
        public required string UserName { get; init; }
        public required bool Success { get; init; }
        public required string Status { get; init; }
        public bool Cancelled { get; init; }
    }

    public BulkRoleAssignment()
    {
        _serviceClient = SessionManager.Instance.GetClient();
        _cts = new CancellationTokenSource();
        _monitorCts = new CancellationTokenSource();

        // Initialize key monitoring task
        _monitorTask = Task.Run(async () =>
        {
            try
            {
                while (!_monitorCts.Token.IsCancellationRequested)
                {
                    if (Console.KeyAvailable && Console.ReadKey(true).Key == ConsoleKey.Q)
                    {
                        _cts.Cancel();
                        Console.WriteLine("\nCancellation requested. Completing current operation...");
                        break;
                    }
                    await Task.Delay(100, _monitorCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Task was cancelled, this is expected
            }
        }, _monitorCts.Token);
    }

    public void Dispose()
    {
        _monitorCts.Cancel();
        _monitorTask.Wait();
        _monitorCts.Dispose();
        _cts.Dispose();

        // Clear any remaining key presses
        while (Console.KeyAvailable)
        {
            Console.ReadKey(true);
        }
    }

    public async Task<List<RoleAssignmentResult>> RunAsync()
    {
        var results = new ConcurrentBag<RoleAssignmentResult>();

        try
        {
            // Initial user prompt
            while (true)
            {
                Console.WriteLine("\nThis process will assign a role to multiple users from a text file.");
                Console.WriteLine("The text file should contain one full name per line (e.g., 'John Smith').");
                Console.WriteLine("Users will be searched by their name as shown in Dynamics.");
                Console.WriteLine("Press 'q' at any time to cancel the operation.");
                Console.Write("\nDo you want to proceed? (y/n): ");

                var response = Console.ReadLine()?.ToLower();
                if (response == "n") return results.ToList();
                if (response != "y")
                {
                    Console.WriteLine("Invalid input. Please enter 'y' or 'n'.");
                    continue;
                }

                // File selection
                var filePath = await SelectFileAsync();
                if (string.IsNullOrEmpty(filePath)) return results.ToList();

                // Read usernames from file
                var usernames = await File.ReadAllLinesAsync(filePath, _cts.Token);
                if (!usernames.Any())
                {
                    Console.WriteLine("No names found in the file.");
                    return results.ToList();
                }

                // Get role from user
                var role = await GetAndConfirmRoleAsync();
                if (role == null) return results.ToList();

                // Process each user
                await ProcessUsersAsync(usernames, role, results);

                // Display results
                if (results.Any())
                {
                    Console.WriteLine("\nPress any key to see detailed results...");
                    Console.ReadKey(true);
                    Console.Clear();
                    Console.WriteLine("=== Detailed Results ===");
                    foreach (var result in results)
                    {
                        Console.WriteLine($"\nUser: {result.UserName}");
                        Console.ForegroundColor = result.Success ? ConsoleColor.Green : ConsoleColor.Red;
                        Console.WriteLine($"Status: {result.Status}");
                        Console.ResetColor();
                        if (result.Cancelled)
                        {
                            Console.ForegroundColor = ConsoleColor.Yellow;
                            Console.WriteLine("Operation was cancelled");
                            Console.ResetColor();
                        }
                    }
                    Console.WriteLine("\nPress any key to continue...");
                    Console.ReadKey(true);
                }

                break;
            }
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("\nOperation was cancelled by user.");
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            Console.WriteLine($"Error: {ex.Message}");
            Console.ResetColor();
        }

        return results.ToList();
    }

    private async Task<string?> SelectFileAsync()
    {
        var tcs = new TaskCompletionSource<string?>();

        await Task.Run(() =>
        {
            var thread = new Thread(() =>
            {
                using var form = new Form();
                form.Visible = false;
                using var dialog = new OpenFileDialog
                {
                    Filter = "Text files (*.txt)|*.txt|All files (*.*)|*.*",
                    Title = "Select file containing full names"
                };

                if (dialog.ShowDialog(form) == DialogResult.OK)
                {
                    tcs.SetResult(dialog.FileName);
                }
                else
                {
                    tcs.SetResult(null);
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();
        }, _cts.Token);

        return await tcs.Task;
    }

    private async Task<Entity?> GetAndConfirmRoleAsync()
    {
        while (true)
        {
            Console.Write("\nEnter the exact role name to assign (as it's shown in dynamics): ");
            var roleName = Console.ReadLine()?.Trim();
            if (string.IsNullOrWhiteSpace(roleName)) return null;

            _cts.Token.ThrowIfCancellationRequested();

            // Search for the role
            var role = await FindRoleAsync(roleName);
            if (role == null)
            {
                Console.WriteLine("No matching role found.");
                continue;
            }

            // Show found role and confirm
            Console.WriteLine($"\nFound role: {role.GetAttributeValue<string>("name")}");
            Console.Write("Is this the correct role? (y/n): ");

            var confirm = Console.ReadLine()?.ToLower();
            if (confirm == "y") return role;
            if (confirm == "n") return null;

            Console.WriteLine("Invalid input. Please enter 'y' or 'n'.");
        }
    }

    private async Task<Entity?> FindRoleAsync(string roleName)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name", "businessunitid"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), _cts.Token);

        return result.Entities.FirstOrDefault();
    }

    private async Task ProcessUsersAsync(string[] userNames, Entity role, ConcurrentBag<RoleAssignmentResult> results)
    {
        Console.WriteLine("\nProcessing users...");
        var total = userNames.Length;
        var current = 0;

        await Parallel.ForEachAsync(userNames,
            new ParallelOptions
            {
                MaxDegreeOfParallelism = 3,
                CancellationToken = _cts.Token
            },
            async (fullName, token) =>
            {
                try
                {
                    token.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(fullName)) return;

                    var processedName = fullName.Trim();
                    Interlocked.Increment(ref current);
                    Console.WriteLine($"\nProcessing: {processedName} ({current}/{total})");

                    // Find user by full name
                    var user = await FindUserByFullNameAsync(processedName);
                    if (user == null)
                    {
                        results.Add(new RoleAssignmentResult
                        {
                            UserName = processedName,
                            Success = false,
                            Status = "User not found",
                            Cancelled = false
                        });
                        return;
                    }

                    // Find equivalent role in user's business unit
                    var userBuId = ((EntityReference)user["businessunitid"]).Id;
                    var equivalentRole = await FindRoleInBusinessUnitAsync(
                        role.GetAttributeValue<string>("name"),
                        userBuId);

                    if (equivalentRole == null)
                    {
                        results.Add(new RoleAssignmentResult
                        {
                            UserName = processedName,
                            Success = false,
                            Status = "Role not found in business unit",
                            Cancelled = false
                        });
                        return;
                    }

                    // Check if user already has the role
                    if (await UserHasRoleAsync(user.Id, equivalentRole.Id))
                    {
                        results.Add(new RoleAssignmentResult
                        {
                            UserName = processedName,
                            Success = true,
                            Status = "Already has role",
                            Cancelled = false
                        });
                        return;
                    }

                    // Assign role
                    await AssignRoleToUserAsync(user.Id, equivalentRole.Id);
                    results.Add(new RoleAssignmentResult
                    {
                        UserName = processedName,
                        Success = true,
                        Status = "Role assigned successfully",
                        Cancelled = false
                    });
                }
                catch (OperationCanceledException)
                {
                    results.Add(new RoleAssignmentResult
                    {
                        UserName = fullName,
                        Success = false,
                        Status = "Operation cancelled",
                        Cancelled = true
                    });
                }
                catch (Exception ex)
                {
                    results.Add(new RoleAssignmentResult
                    {
                        UserName = fullName,
                        Success = false,
                        Status = $"Error: {ex.Message}",
                        Cancelled = false
                    });
                }
            });

        // Display summary
        var successful = results.Count(r => r.Success);
        var cancelled = results.Count(r => r.Cancelled);
        var failed = results.Count(r => !r.Success && !r.Cancelled);

        Console.WriteLine("\nOperation completed.");
        Console.WriteLine($"Successfully processed: {successful}");
        Console.WriteLine($"Failed: {failed}");
        Console.WriteLine($"Cancelled: {cancelled}");
    }

    private async Task<Entity?> FindUserByFullNameAsync(string fullName)
    {
        var query = new QueryExpression("systemuser")
        {
            ColumnSet = new ColumnSet("systemuserid", "businessunitid", "fullname"),
            Criteria = new FilterExpression
            {
                FilterOperator = LogicalOperator.And,
                Conditions =
                {
                    new ConditionExpression("fullname", ConditionOperator.BeginsWith, fullName.Trim()),
                    new ConditionExpression("isdisabled", ConditionOperator.Equal, false)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), _cts.Token);

        return result.Entities
            .FirstOrDefault(e => e.GetAttributeValue<string>("fullname")
                .Equals(fullName.Trim(), StringComparison.OrdinalIgnoreCase));
    }

    private async Task<Entity?> FindRoleInBusinessUnitAsync(string roleName, Guid businessUnitId)
    {
        var key = (roleName, businessUnitId);
        if (_roleCache.TryGetValue(key, out var cachedRole))
            return cachedRole;

        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid", "name"),
            Criteria = new FilterExpression
            {
                Conditions =
                {
                    new ConditionExpression("name", ConditionOperator.Equal, roleName),
                    new ConditionExpression("businessunitid", ConditionOperator.Equal, businessUnitId)
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), _cts.Token);
        var role = result.Entities.FirstOrDefault();

        if (role != null)
            _roleCache[key] = role;

        return role;
    }

    private async Task<bool> UserHasRoleAsync(Guid userId, Guid roleId)
    {
        var query = new QueryExpression("role")
        {
            ColumnSet = new ColumnSet("roleid"),
            Criteria = new FilterExpression
            {
                Conditions = { new ConditionExpression("roleid", ConditionOperator.Equal, roleId) }
            },
            LinkEntities =
            {
                new LinkEntity
                {
                    LinkFromEntityName = "role",
                    LinkToEntityName = "systemuserroles",
                    LinkFromAttributeName = "roleid",
                    LinkToAttributeName = "roleid",
                    LinkEntities =
                    {
                        new LinkEntity
                        {
                            LinkFromEntityName = "systemuserroles",
                            LinkToEntityName = "systemuser",
                            LinkFromAttributeName = "systemuserid",
                            LinkToAttributeName = "systemuserid",
                            LinkCriteria = new FilterExpression
                            {
                                Conditions =
                                {
                                    new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                                }
                            }
                        }
                    }
                }
            }
        };

        var result = await Task.Run(() => _serviceClient.RetrieveMultiple(query), _cts.Token);
        return result.Entities.Any();
    }

    private async Task AssignRoleToUserAsync(Guid userId, Guid roleId)
    {
        var request = new AssociateRequest
        {
            Target = new EntityReference("systemuser", userId),
            RelatedEntities = new EntityReferenceCollection
            {
                new EntityReference("role", roleId)
            },
            Relationship = new Relationship("systemuserroles_association")
        };

        await Task.Run(() => _serviceClient.Execute(request), _cts.Token);
    }
}