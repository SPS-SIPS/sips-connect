import React, { useState } from 'react';

import styles from '../styles/RequestBuilder.module.css';
import { FiCopy } from 'react-icons/fi';
import useEndpoints from "../api/hooks/useEndpoints";
import SelectInput from "../component/common/SelectInput/SelectInput";
import RequestForm from "../component/RequestForm/RequestForm";
import Head from "next/head";
import RoleGuard from "../auth/RoleGuard";

const RequestBuilder = () => {
    const { endpoints, loading, error } = useEndpoints();
    const [selectedRequest, setSelectedRequest] = useState<string>('');
    const [generatedJson, setGeneratedJson] = useState<string>('');
    const [copied, setCopied] = useState<boolean>(false);

    const handleSubmit = (data: Record<string, string>) => {
        const jsonString = JSON.stringify(data, null, 2);
        setGeneratedJson(jsonString);
        setCopied(false);
    };

    const copyToClipboard = () => {
        navigator.clipboard.writeText(generatedJson);
        setCopied(true);
        setTimeout(() => setCopied(false), 2000);
    };

    if (loading) return <div className={styles.loading}>Loading endpoints...</div>;
    if (error) return <div className={styles.error}>Error loading endpoints: {error}</div>;
    if (!endpoints) return <div className={styles.noEndpoints}>No endpoints available</div>;

    const requestOptions = Object.keys(endpoints)
        .filter(endpoint => endpoint.endsWith('Request'))
        .map(endpoint => ({
            value: endpoint,
            label: endpoint
        }));

    return (
       <RoleGuard allowedRoles={['gateway']}>
           <Head>
               <title>API Request JSON Builder</title>
               <meta name="description" content="Build and preview API request JSON payloads. Customize your API requests with ease and generate properly formatted request data." />
           </Head>
           <div className={styles.requestBuilder}>
               <div className={styles.header}>
                   <h1 className={styles.title}>API Request JSON Builder</h1>
                   <p className={styles.subtitle}>
                       Preview exactly how your API requests should be structured in JSON format.
                       Fill in the fields below to generate properly formatted request payloads.
                   </p>
               </div>
               <div className={styles.requestSelector}>
                   <SelectInput
                       label="Select Request Type"
                       value={selectedRequest}
                       onChange={(e) => {
                           setSelectedRequest(e.target.value);
                           setGeneratedJson('');
                       }}
                       options={requestOptions}
                       required
                   />
               </div>

               {selectedRequest && (
                   <>
                       <RequestForm
                           requestType={selectedRequest}
                           fieldMappings={endpoints[selectedRequest].fieldMappings}
                           onSubmit={handleSubmit}
                           buttonText={"Generate Request"}
                       />

                       {generatedJson && (
                           <div className={styles.jsonPreview}>
                               <div className={styles.jsonHeader}>
                                   <h3>Generated JSON</h3>
                                   <button
                                       onClick={copyToClipboard}
                                       className={styles.copyButton}
                                       aria-label="Copy JSON to clipboard"
                                   >
                                       <FiCopy />
                                       {copied ? 'Copied!' : 'Copy'}
                                   </button>
                               </div>
                               <pre className={styles.jsonContent}>
                                {generatedJson}
                            </pre>
                           </div>
                       )}
                   </>
               )}
           </div>
       </RoleGuard>
    );
};

export default RequestBuilder;