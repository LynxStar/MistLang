properties([
    disableConcurrentBuilds(),
    buildDiscarder(logRotator(numToKeepStr: '30', artifactNumToKeepStr: '10')),
    pipelineTriggers([pollSCM('H/5 * * * *')])
])

timestamps {
    timeout(time: 15, unit: 'MINUTES') {
        node('portable && linux') {
            def revision
            stage('Checkout main') {
                deleteDir()
                revision = checkout([
                    $class: 'GitSCM',
                    branches: [[name: '*/main']],
                    userRemoteConfigs: [[url: 'https://github.com/LynxStar/MistLang.git']]
                ]).GIT_COMMIT
                currentBuild.description = "${revision.take(12)} → mistlang.com"
            }
            stage('Build book') {
                sh 'bash book/publish.sh'
                withEnv(["BOOK_REVISION=${revision}"]) {
                    sh 'printf "%s\n" "$BOOK_REVISION" > book/output/revision.txt'
                }
                sh 'tar -czf book/site.tar.gz -C book/output .'
                archiveArtifacts artifacts: 'book/site.tar.gz', fingerprint: true
            }
            stage('Publish book') {
                withCredentials([sshUserPrivateKey(credentialsId: 'mist-book-isengard-deploy', keyFileVariable: 'BOOK_SSH_KEY', usernameVariable: 'BOOK_SSH_USER')]) {
                    withEnv(["BOOK_REVISION=${revision}"]) {
                        sh 'bash book/ci/deploy.sh'
                        sh 'test "$(curl --fail --silent --show-error http://192.168.1.6:8090/revision.txt)" = "$BOOK_REVISION"'
                    }
                }
            }
        }
    }
}
