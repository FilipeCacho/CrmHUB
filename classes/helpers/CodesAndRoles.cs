using System.Collections.Generic;

    internal class CodesAndRoles
    {

       public static readonly HashSet<string> CountryCodeNA = new HashSet<string>
        {
            "US","CA","MX","NA"
        };


        public static readonly HashSet<string> CountryCodeEU = new HashSet<string>
        {
            "AD", "AE", "AF", "AG", "AI", "AL", "AM", "AO", "AQ", "AR", "AS", "AT", "AU", "AW", "AX", "AZ",
            "BA", "BB", "BD", "BE", "BF", "BG", "BH", "BI", "BJ", "BL", "BM", "BN", "BO", "BQ", "BR", "BS",
            "BT", "BV", "BW", "BY", "BZ", "CC", "CD", "CF", "CG", "CH", "CI", "CK", "CL", "CM", "CN",
            "CO", "CR", "CU", "CV", "CW", "CX", "CY", "CZ", "DE", "DJ", "DK", "DM", "DO", "DZ", "EC", "EE",
            "EG", "EH", "ER", "ES", "ET", "FI", "FJ", "FK", "FM", "FO", "FR", "GA", "GB", "GD", "GE", "GF",
            "GG", "GH", "GI", "GL", "GM", "GN", "GP", "GQ", "GR", "GS", "GT", "GU", "GW", "GY", "HK", "HM",
            "HN", "HR", "HT", "HU", "ID", "IE", "IL", "IM", "IN", "IO", "IQ", "IR", "IS", "IT", "JE", "JM",
            "JO", "JP", "KE", "KG", "KH", "KI", "KM", "KN", "KP", "KR", "KW", "KY", "KZ", "LA", "LB", "LC",
            "LI", "LK", "LR", "LS", "LT", "LU", "LV", "LY", "MA", "MC", "MD", "ME", "MF", "MG", "MH", "MK",
            "ML", "MM", "MN", "MO", "MP", "MQ", "MR", "MS", "MT", "MU", "MV", "MW", "MY", "MZ", "NA",
            "NC", "NE", "NF", "NG", "NI", "NL", "NO", "NP", "NR", "NU", "NZ", "OM", "PA", "PE", "PF", "PG",
            "PH", "PK", "PL", "PM", "PN", "PR", "PS", "PT", "PW", "PY", "QA", "RE", "RO", "RS", "RU", "RW",
            "SA", "SB", "SC", "SD", "SE", "SG", "SH", "SI", "SJ", "SK", "SL", "SM", "SN", "SO", "SR", "SS",
            "ST", "SV", "SX", "SY", "SZ", "TC", "TD", "TF", "TG", "TH", "TJ", "TK", "TL", "TM", "TN", "TO",
            "TR", "TT", "TV", "TW", "TZ", "UA", "UG", "UM", "UY", "UZ", "VA", "VC", "VE", "VG", "VI",
            "VN", "VU", "WF", "WS", "YE", "YT", "ZA", "ZM", "ZW","EUR"
        };
        
        //Team creation default Admin & Team Roles
        public static readonly string AdministratorNameEU = "JORGE FELIX AVELLANAL APAOLAZA";
        public static readonly string AdministradorNameUS = "GREG EMANDI";
        public static readonly string[] TeamRolesEU = { "EDPR_ROL_EUROPA", "EDPR_ROL_Field Service_Resource", "EDPR_ROL_GENERAL" };
        public static readonly string[] ProprietaryTeamRoles = { "EDPR_Rol para equipos propietarios de unidades de negocio" };

        //User normalizer teams & Roles
        public static readonly string[] EUDefaultRolesForExternalUsers = { "EDPR_ROL_EUROPA", "EDPR_ROL_Field Service_Resource", "EDPR_ROL_GENERAL" };
        public static readonly string[] EUDefaultRolesForInternalUsers = { "EDPR_ROL_EUROPA", "EDPR_ROL_Field Service_Resource", "EDPR_ROL_GENERAL", "EDPR_personal_interno", "Resco Archive Read" };

        public static readonly string[] EUDefaultTeamsForExternalUsers = { "Equipo gestión conocimiento contratas" };
        public static readonly string[] EUDefaultTeamsForInteralUsers = { "Equipo gestión conocimiento personal interno" };

        public static readonly string[] EUDefaultTeamForPortugueseAndSpanishUsers = { "Equipo dashboard contratas ES y PT" };

        public static readonly string[] NADefaultRolesForInternalUser = { "EDPR_ROL_USA", "EDPR_ROL_Field Service_Resource", "EDPR_ROL_GENERAL", "EDPR_personal_interno", "Resco Archive Read" };

        public static readonly string[] EURegion = { "EU", "300000000" };
        public static readonly string[] NARegion = { "NA", "300000001" };

        //Resco Acess
        public static readonly string[] RescoTeamEU = { "Equipo templates checklist EUR" };
        public static readonly string[] RescoTeamNA = { "Equipo templates checklist NA" };
        public static readonly string[] RescoRole = { "EDPR_INSPECTIONS" };

        //new user workflow name
        public static readonly string NewUserWorkflow = "Usuario-Proceso para crear un recurso desde el usuario";
    }  





