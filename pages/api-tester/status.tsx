import ApiRequestTester from "../../component/ApiRequestTester/ApiRequestTester";
import RoleGuard from "../../auth/RoleGuard";

const Status= () => {

    return (
        <RoleGuard allowedRoles={['gateway']}>
            <ApiRequestTester
                title={"API Status Request Tester "}
                placeholder={"https://example.com/api/v1/Gateway/Status"}
                selectedRequest={"StatusRequest"}
                initialUrl={"http://localhost:8081/api/v1/Gateway/Status"}
            />
        </RoleGuard>
    );
};

export default Status;