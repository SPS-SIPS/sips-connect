import { EndpointsData, FieldMapping } from "../api/hooks/useEndpoints";

export function getUserFieldFromEndpoint(
    endpointsData: EndpointsData | null,
    endpointName: string,
    internalFieldName: string
): string | undefined {
    return endpointsData?.[endpointName]?.fieldMappings.find(
        (field: FieldMapping) => field.internalField === internalFieldName
    )?.userField;
}

export function getUserFieldsFromEndpoint(
    endpointsData: EndpointsData | null,
    endpointName: string,
    internalFieldNames: string[]
): Record<string, string | undefined> {
    const mappings = endpointsData?.[endpointName]?.fieldMappings || [];
    const result: Record<string, string | undefined> = {};

    internalFieldNames.forEach((internalField) => {
        result[internalField] = mappings.find(
            (field: FieldMapping) => field.internalField === internalField
        )?.userField;
    });

    return result;
}

export function extractFieldsFromData(
    data: Record<string, string> | null,
    fields: (string | undefined)[]
): Record<string, string> {
    if (!data) return {};
    return fields.reduce((result, key) => {
        if (key && key in data) {
            result[key] = data[key];
        }
        return result;
    }, {} as Record<string, string>);
}

export function remapToInternalFields(
    data: Record<string, string>,
    fieldMap: Record<string, string | undefined>
): Record<string, string> {
    const reversedMap = Object.entries(fieldMap).reduce((acc, [internal, user]) => {
        if (user) acc[user] = internal;
        return acc;
    }, {} as Record<string, string>);

    return Object.entries(data).reduce((acc, [key, value]) => {
        const internalKey = reversedMap[key] || key;
        acc[internalKey] = value;
        return acc;
    }, {} as Record<string, string>);
}


export function mapToPrefilledValues(data: {
    ToBIC: string;
    Id: string;
    Type: string;
    Name: string;
    Currency: string;
}) {
    return {
        CreditorAgentBIC: data.ToBIC,
        CreditorAccount: data.Id,
        CreditorAccountType: data.Type,
        CreditorName: data.Name,
        Currency: data.Currency,
    };
}