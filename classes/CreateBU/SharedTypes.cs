using System.Collections.Generic;

    public struct TransformedTeamData
    {
        public string Bu { get; set; }
        public string EquipaContrata { get; set; }
        public string ContractorCode { get; set; }
        public string EquipaEDPR { get; set; }
        public string PlannerGroup { get; set; }
        public string PlannerCenterName { get; set; }
        public string PrimaryCompany { get; set; }
        public string Contractor { get; set; }
        public string EquipaContrataContrata { get; set; }
        public string FileName { get; set; }
        public string FullBuName { get; set; }  

    }

    public class BuUserDomains
    {
        public string NewCreatedPark { get; set; }
        public List<string> UserDomains { get; set; }
    }

    public struct UserData
    {
        public string YomiFullName { get; set; }
        public string DomainName { get; set; }
        public string BusinessUnit { get; set; }
        public string Team { get; set; }
    }
