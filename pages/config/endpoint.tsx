import React, { useState } from 'react';
import useEndpoints from "../../api/hooks/useEndpoints";
import useUpdateEndpoints from "../../api/hooks/useUpdateEndpoints";
import Input from "../../component/common/Input/Input";
import AlertModal from "../../component/common/AlertModal/AlertModal";
import styles from "../../styles/EndpointsPage.module.css";
import Head from "next/head";
import ProtectedRoute from "../../component/common/ProtectedRoute";

type FieldMapping = {
    internalField: string;
    userField: string;
    type: string;
};

type Endpoint = {
    fieldMappings: FieldMapping[];
};

type EndpointData = {
    [key: string]: Endpoint;
};

type EndpointUpdatePayload = {
    Endpoints: {
        [key: string]: {
            FieldMappings: Array<{
                InternalField: string;
                UserField: string;
                Type?: string;
            }>;
        };
    };
};

const EndpointsPage = () => {
    const { endpoints: initialEndpoints, loading, error } = useEndpoints();
    const { updateEndpoints, loading: updateLoading, error: updateError } = useUpdateEndpoints();
    const [endpoints, setEndpoints] = useState<EndpointData | null>(null);
    const [editingEndpoint, setEditingEndpoint] = useState<string | null>(null);
    const [localChanges, setLocalChanges] = useState<EndpointData>({});
    const [showErrorModal, setShowErrorModal] = useState(false);

    React.useEffect(() => {
        if (initialEndpoints) {
            setEndpoints(initialEndpoints);
        }
    }, [initialEndpoints]);

    // Show modal when there's an update error
    React.useEffect(() => {
        if (updateError) {
            setShowErrorModal(true);
        }
    }, [updateError]);

    const handleUserFieldChange = (endpointName: string, internalField: string, newValue: string) => {
        setLocalChanges(prev => {
            const endpointChanges = prev[endpointName] || {
                ...(endpoints?.[endpointName] || { fieldMappings: [] })
            };

            const updatedMappings = endpointChanges.fieldMappings.map(mapping =>
                mapping.internalField === internalField
                    ? { ...mapping, userField: newValue }
                    : mapping
            );

            return {
                ...prev,
                [endpointName]: {
                    ...endpointChanges,
                    fieldMappings: updatedMappings
                }
            };
        });
    };

    const handleStartEditing = (endpointName: string) => {
        setEditingEndpoint(endpointName);
        if (!localChanges[endpointName] && endpoints?.[endpointName]) {
            setLocalChanges(prev => ({
                ...prev,
                [endpointName]: { ...endpoints[endpointName] }
            }));
        }
    };

    const handleCancelEditing = (endpointName: string) => {
        setEditingEndpoint(null);
        setLocalChanges(prev => {
            const newChanges = { ...prev };
            delete newChanges[endpointName];
            return newChanges;
        });
    };

    const handleSaveEndpoint = async (endpointName: string) => {
        if (!localChanges[endpointName]) return;

        const payload: EndpointUpdatePayload = {
            Endpoints: {
                [endpointName]: {
                    FieldMappings: localChanges[endpointName].fieldMappings.map(mapping => ({
                        InternalField: mapping.internalField,
                        UserField: mapping.userField,
                        Type: mapping.type
                    }))
                }
            }
        };

        try {
            await updateEndpoints(payload);
            setEndpoints(prev => ({
                ...prev!,
                [endpointName]: localChanges[endpointName]
            }));
            handleCancelEditing(endpointName);
        } catch (err) {
            console.error("Failed to update endpoint:", err);
        }
    };

    const closeErrorModal = () => {
        setShowErrorModal(false);
    };

    if (loading) return <div className={styles.loading}>Loading...</div>;
    if (error) return <div className={styles.error}>Error: {error}</div>;
    if (!endpoints) return <div className={styles.noEndpoints}>No endpoints found</div>;

    return (
       <ProtectedRoute>
           <Head>
               <title>API Request & Response Mapping Editor</title>
               <meta name="description" content="SPS Connect Platform facilitates seamless transactions between SIPS SVIP and local banking systems through secure ISO 20022 message translation and integration with local banking JSON APIs." />
           </Head>
           <div className={styles.container}>
               {/* Error Modal */}
               {showErrorModal && updateError && (
                   <AlertModal
                       title="Update Error"
                       message={updateError}
                       onConfirm={closeErrorModal}
                       error={true}
                       buttonText="OK"
                   />
               )}

               <div className={styles.header}>
                   <h1 className={styles.title}>API Request & Response Mapping Editor</h1>
                   <p className={styles.subtitle}>
                       Customize how internal fields map to your API request/response JSON structure
                   </p>
               </div>

               {Object.entries(endpoints).map(([endpointName, endpoint]) => {
                   const isEditing = editingEndpoint === endpointName;
                   const endpointChanges = localChanges[endpointName] || endpoint;

                   return (
                       <div key={endpointName} className={styles.endpointContainer}>
                           <div className={styles.endpointHeader}>
                               <h3 className={styles.endpointTitle}>{endpointName}</h3>
                               <div className={styles.endpointActions}>
                                   {!isEditing ? (
                                       <button
                                           onClick={() => handleStartEditing(endpointName)}
                                           className={styles.editButton}
                                       >
                                           Edit
                                       </button>
                                   ) : (
                                       <>
                                           <button
                                               onClick={() => handleSaveEndpoint(endpointName)}
                                               className={styles.saveButton}
                                               disabled={updateLoading}
                                           >
                                               {updateLoading ? 'Saving...' : 'Save'}
                                           </button>
                                           <button
                                               onClick={() => handleCancelEditing(endpointName)}
                                               className={styles.cancelButton}
                                           >
                                               Cancel
                                           </button>
                                       </>
                                   )}
                               </div>
                           </div>

                           <div className={styles.mappingsGrid}>
                               {endpointChanges.fieldMappings.map((mapping) => (
                                   <div key={mapping.internalField} className={styles.mappingItem}>
                                       <Input
                                           label={`${mapping.internalField} (${mapping.type})`}
                                           value={mapping.userField}
                                           onChange={(e) => handleUserFieldChange(
                                               endpointName,
                                               mapping.internalField,
                                               e.target.value
                                           )}
                                           type="text"
                                           disabled={!isEditing}
                                       />
                                   </div>
                               ))}
                           </div>
                       </div>
                   );
               })}
           </div>
       </ProtectedRoute>
    );
};

export default EndpointsPage;