namespace i25Core.Web.Versioning
{
    /// <summary>Bound from the "ApiVersioning" section of appsettings.json.</summary>
    public class ApiVersioningSettings
    {
        public const string SectionName = "ApiVersioning";

        /// <summary>The version a request gets when it names none (and AssumeDefaultVersionWhenUnspecified is true). Also the version of every controller that has no [ApiVersion].</summary>
        public string DefaultVersion { get; set; } = "1.0";

        /// <summary>
        /// true (default): /api/Vendor works and means the default version, so existing clients keep working.
        /// false: every request must name a version (/api/v1/Vendor); unversioned calls get a 400.
        /// </summary>
        public bool AssumeDefaultVersionWhenUnspecified { get; set; } = true;

        /// <summary>Also accept the version in a request header (for example X-Api-Version: 2.0).</summary>
        public bool ReadFromHeader { get; set; } = true;

        public string HeaderName { get; set; } = "X-Api-Version";

        /// <summary>Add api-supported-versions and api-deprecated-versions headers to responses.</summary>
        public bool ReportApiVersions { get; set; } = true;
    }
}
