// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.CommandLine.Invocation;
using Microsoft.Data.Sqlite;
using Microsoft.OpenApi.Models;
using Microsoft.OpenApi.Readers;
using Microsoft365.DeveloperProxy.Abstractions;

namespace Microsoft365.DeveloperProxy;

public class MSGraphDbCommandHandler : ICommandHandler
{
    private readonly ILogger _logger;
    private static readonly Dictionary<string, OpenApiDocument> _openApiDocuments = new();
    private static readonly string[] graphVersions = new[] { "v1.0", "beta" };

    private static string GetGraphOpenApiYamlFileName(string version) => $"graph-{version.Replace(".", "_")}-openapi.yaml";

    public MSGraphDbCommandHandler(ILogger logger)
    {
        _logger = logger;
    }

    public int Invoke(InvocationContext context)
    {
        return InvokeAsync(context).GetAwaiter().GetResult();
    }

    public async Task<int> InvokeAsync(InvocationContext context)
    {
        return await GenerateMsGraphDb(_logger);
    }

    public static async Task<int> GenerateMsGraphDb(ILogger logger, bool skipIfUpdatedToday = false)
    {
        var appFolder = ProxyUtils.AppFolder;
        if (string.IsNullOrEmpty(appFolder))
        {
            logger.LogError("App folder not found");
            return 1;
        }

        try
        {
            var dbFileInfo = new FileInfo(ProxyUtils.MsGraphDbFilePath);
            var modifiedToday = dbFileInfo.Exists && dbFileInfo.LastWriteTime.Date == DateTime.Now.Date;
            if (modifiedToday && skipIfUpdatedToday)
            {
                logger.LogInfo("Microsoft Graph database already updated today");
                return 1;
            }

            await UpdateOpenAPIGraphFilesIfNecessary(appFolder, logger);
            await LoadOpenAPIFiles(appFolder, logger);
            if (_openApiDocuments.Count < 1)
            {
                logger.LogDebug("No OpenAPI files found or couldn't load them");
                return 1;
            }

            var dbConnection = ProxyUtils.MsGraphDbConnection;
            CreateDb(dbConnection, logger);
            FillData(dbConnection, logger);

            logger.LogInfo("Microsoft Graph database successfully updated");

            return 0;
        }
        catch (Exception ex)
        {
            logger.LogError(ex.Message);
            return 1;
        }

    }

    private static void CreateDb(SqliteConnection dbConnection, ILogger logger)
    {
        logger.LogInfo("Creating database...");

        logger.LogDebug("Dropping endpoints table...");
        var dropTable = dbConnection.CreateCommand();
        dropTable.CommandText = "DROP TABLE IF EXISTS endpoints";
        dropTable.ExecuteNonQuery();

        logger.LogDebug("Creating endpoints table...");
        var createTable = dbConnection.CreateCommand();
        // when you change the schema, increase the db version number in ProxyUtils
        createTable.CommandText = "CREATE TABLE IF NOT EXISTS endpoints (path TEXT, graphVersion TEXT, hasSelect BOOLEAN)";
        createTable.ExecuteNonQuery();

        logger.LogDebug("Creating index on endpoints and version...");
        // Add an index on the path and graphVersion columns
        var createIndex = dbConnection.CreateCommand();
        createIndex.CommandText = "CREATE INDEX IF NOT EXISTS idx_endpoints_path_version ON endpoints (path, graphVersion)";
        createIndex.ExecuteNonQuery();
    }

    private static void FillData(SqliteConnection dbConnection, ILogger logger)
    {
        logger.LogInfo("Filling database...");

        var i = 0;

        foreach (var openApiDocument in _openApiDocuments)
        {
            var graphVersion = openApiDocument.Key;
            var document = openApiDocument.Value;

            logger.LogDebug($"Filling database for {graphVersion}...");

            var insertEndpoint = dbConnection.CreateCommand();
            insertEndpoint.CommandText = "INSERT INTO endpoints (path, graphVersion, hasSelect) VALUES (@path, @graphVersion, @hasSelect)";
            insertEndpoint.Parameters.Add(new SqliteParameter("@path", null));
            insertEndpoint.Parameters.Add(new SqliteParameter("@graphVersion", null));
            insertEndpoint.Parameters.Add(new SqliteParameter("@hasSelect", null));

            foreach (var path in document.Paths)
            {
                logger.LogDebug($"Endpoint {graphVersion}{path.Key}...");

                // Get the GET operation for this path
                var getOperation = path.Value.Operations.FirstOrDefault(o => o.Key == OperationType.Get).Value;
                if (getOperation == null)
                {
                    logger.LogDebug($"No GET operation found for {graphVersion}{path.Key}");
                    continue;
                }

                // Check if the GET operation has a $select parameter
                var hasSelect = getOperation.Parameters.Any(p => p.Name == "$select");

                logger.LogDebug($"Inserting endpoint {graphVersion}{path.Key} with hasSelect={hasSelect}...");
                insertEndpoint.Parameters["@path"].Value = path.Key;
                insertEndpoint.Parameters["@graphVersion"].Value = graphVersion;
                insertEndpoint.Parameters["@hasSelect"].Value = hasSelect;
                insertEndpoint.ExecuteNonQuery();
                i++;
            }
        }

        logger.LogInfo($"Inserted {i} endpoints in the database");
    }

    private static async Task UpdateOpenAPIGraphFilesIfNecessary(string folder, ILogger logger)
    {
        logger.LogInfo("Checking for updated OpenAPI files...");

        foreach (var version in graphVersions)
        {
            try
            {
                var file = new FileInfo(Path.Combine(folder, GetGraphOpenApiYamlFileName(version)));
                logger.LogDebug($"Checking for updated OpenAPI file {file}...");
                if (file.Exists && file.LastWriteTime.Date == DateTime.Now.Date)
                {
                    logger.LogInfo($"File {file} already updated today");
                    continue;
                }

                var url = $"https://raw.githubusercontent.com/microsoftgraph/msgraph-metadata/master/openapi/{version}/openapi.yaml";
                logger.LogDebug($"Downloading OpenAPI file from {url}...");

                var client = new HttpClient();
                var response = await client.GetStringAsync(url);
                File.WriteAllText(file.FullName, response);

                logger.LogInfo($"Downloaded OpenAPI file from {url} to {file}");
            }
            catch (Exception ex)
            {
                logger.LogError(ex.Message);
            }
        }
    }

    private static async Task LoadOpenAPIFiles(string folder, ILogger logger)
    {
        logger.LogInfo("Loading OpenAPI files...");

        foreach (var version in graphVersions)
        {
            var filePath = Path.Combine(folder, GetGraphOpenApiYamlFileName(version));
            var file = new FileInfo(filePath);
            logger.LogDebug($"Loading OpenAPI file for {filePath}...");

            if (!file.Exists)
            {
                logger.LogDebug($"File {filePath} does not exist");
                continue;
            }

            try
            {
                var openApiDocument = await new OpenApiStreamReader().ReadAsync(file.OpenRead());
                _openApiDocuments[version] = openApiDocument.OpenApiDocument;

                logger.LogDebug($"Added OpenAPI file {filePath} for {version}");
            }
            catch (Exception ex)
            {
                logger.LogError($"Error loading OpenAPI file {filePath}: {ex.Message}");
            }
        }
    }
}
